using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor;

public partial class LibraryBrowserWindow : Window
{
    private readonly LibrarySettings _librarySettings;
    private readonly MetadataService _metadataService;
    private readonly NormalizationService _normalizationService;
    private AudioPlayerService _audioPlayer => App.PlaybackService;  // Using singleton for playlist population
    private List<LibraryShow> _shows = new();
    private List<LibraryShow> _allShows = new();  // Unfiltered list for search
    private List<TrackInfo> _currentTracks = new();
    private ObservableCollection<ConcertViewItem> _concertViewItems = new();
    private LibraryShow? _currentShow = null;
    private string _quickSearchText = "";
    private List<string> _advancedSearchSongs = new();
    private List<string> _advancedSearchExcludedSongs = new();
    private List<string> _advancedSearchSequence = new();
    private Dictionary<string, (string venue, string location)> _dateVenueMap = new();
    private ObservableCollection<string> _concertDates = new();
    private string _selectedDate = "";

    // Public properties for XAML binding
    public Dictionary<string, (string venue, string location)> DateVenueMap => _dateVenueMap;
    public ObservableCollection<string> ConcertDates => _concertDates;

    public string SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                // Note: ComboBox selection is managed via SelectedIndex reset in handler
            }
        }
    }

    public LibraryBrowserWindow()
    {
        InitializeComponent();

        _librarySettings = LibrarySettings.Load();
        _metadataService = new MetadataService();
        _normalizationService = new NormalizationService();

        // Restore window position
        RestoreWindowPosition();

        // Save window position when closing
        Closing += LibraryBrowserWindow_Closing;

        // Subscribe to PlayerWindow visibility changes (when closed via × button)
        Loaded += LibraryBrowserWindow_Loaded;

        // Load shows
        LoadShows();
    }

    private void LibraryBrowserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Find PlayerWindow and subscribe to its IsVisibleChanged event
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window is PlayerWindow playerWindow)
            {
                playerWindow.IsVisibleChanged += PlayerWindow_IsVisibleChanged;

                // Set initial menu item state
                PlayerToggleMenuItem.FontWeight = playerWindow.Visibility == Visibility.Visible
                    ? FontWeights.Bold
                    : FontWeights.Normal;
                break;
            }
        }
    }

    private void PlayerWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is PlayerWindow playerWindow)
        {
            // Update menu item state to reflect PlayerWindow visibility
            PlayerToggleMenuItem.FontWeight = playerWindow.Visibility == Visibility.Visible
                ? FontWeights.Bold
                : FontWeights.Normal;
        }
    }

    private void RestoreWindowPosition()
    {
        if (_librarySettings.LibraryWindowLeft.HasValue && _librarySettings.LibraryWindowTop.HasValue)
        {
            Left = _librarySettings.LibraryWindowLeft.Value;
            Top = _librarySettings.LibraryWindowTop.Value;
        }

        if (_librarySettings.LibraryWindowWidth.HasValue && _librarySettings.LibraryWindowHeight.HasValue)
        {
            Width = _librarySettings.LibraryWindowWidth.Value;
            Height = _librarySettings.LibraryWindowHeight.Value;
        }
    }

    private void LibraryBrowserWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save window position
        _librarySettings.LibraryWindowLeft = Left;
        _librarySettings.LibraryWindowTop = Top;
        _librarySettings.LibraryWindowWidth = Width;
        _librarySettings.LibraryWindowHeight = Height;
        _librarySettings.Save();
    }

    // Media key support - hook into Windows message pump
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source != null)
        {
            source.AddHook(WndProc);
        }
    }

    // Windows message constants for media keys
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int APPCOMMAND_MEDIA_STOP = 13;
    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    // Handle Windows messages for media keys
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APPCOMMAND)
        {
            int cmd = (int)((long)lParam >> 16) & 0xFFF;

            // Media keys now handled by PlayerWindow
            // (These keys will be handled by PlayerWindow which has focus when playing)
            switch (cmd)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE:
                case APPCOMMAND_MEDIA_STOP:
                case APPCOMMAND_MEDIA_NEXTTRACK:
                case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                    // No-op: PlayerWindow handles media keys
                    handled = false;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void LoadShows()
    {
        _shows.Clear();
        _allShows.Clear();

        // Load audience recordings from LibraryRootPath
        if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) &&
            Directory.Exists(_librarySettings.LibraryRootPath))
        {
            LoadAudienceRecordings();
        }

        // Load official releases from OfficialReleasesPath
        if (!string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath) &&
            Directory.Exists(_librarySettings.OfficialReleasesPath))
        {
            LoadOfficialReleases();
        }

        // Check if we loaded anything
        if (_allShows.Count == 0)
        {
            ShowsDataGrid.ItemsSource = _shows;
            StatusText.Text = "No library paths set or no shows found. Go to File > Settings to configure.";
            return;
        }

        // Group folders with matching Album tags into single LibraryShow entries
        GroupMultiFolderAlbums();

        // Sort by type first (Live, Official Release, Studio), then by date/name
        _allShows = _allShows
            .OrderBy(s => s.Type)
            .ThenByDescending(s => s.Type == AlbumType.AudienceRecording ? s.Date : s.AlbumName)
            .ToList();

        // Apply any active search filters
        ApplySearchFilter();
    }

    private void LoadAudienceRecordings()
    {
        // Scan year folders for live recordings
        var topLevelFolders = Directory.GetDirectories(_librarySettings.LibraryRootPath);

        foreach (var topFolder in topLevelFolders)
        {
            // Load live recordings from year folders (skip non-year folders)
            var topFolderName = Path.GetFileName(topFolder);

            // Skip folders that are not year folders (must be 4-digit number)
            if (!System.Text.RegularExpressions.Regex.IsMatch(topFolderName, @"^\d{4}$"))
            {
                continue;
            }

            var showFolders = Directory.GetDirectories(topFolder);

            foreach (var showFolder in showFolders)
            {
                var folderName = Path.GetFileName(showFolder);

                // Expected formats:
                // "yyyy-MM-dd - Venue - City, State"
                // "yyyy-MM-dd - Venue, City, State"
                var parts = folderName.Split(new[] { " - " }, StringSplitOptions.None);

                string date = "";
                string venue = "";
                string city = "";
                string state = "";

                if (parts.Length >= 2)
                {
                    date = parts[0];

                    // Try format: "Date - Venue - City, State"
                    if (parts.Length == 3)
                    {
                        venue = parts[1];
                        var locationParts = parts[2].Split(new[] { ", " }, StringSplitOptions.None);
                        city = locationParts.Length > 0 ? locationParts[0] : "";
                        state = locationParts.Length > 1 ? locationParts[1] : "";
                    }
                    // Try format: "Date - Venue, City, State"
                    else if (parts.Length == 2)
                    {
                        var venueParts = parts[1].Split(new[] { ", " }, StringSplitOptions.None);
                        venue = venueParts.Length > 0 ? venueParts[0] : "";
                        city = venueParts.Length > 1 ? venueParts[1] : "";
                        state = venueParts.Length > 2 ? venueParts[2] : "";
                    }

                    var audioFiles = Directory.GetFiles(showFolder, "*.flac")
                                        .Concat(Directory.GetFiles(showFolder, "*.mp3"))
                                        .ToArray();

                    // Try to read OfficialRelease or BoxSet name from first audio file's album tag
                    string officialRelease = "";
                    string boxSetName = "";
                    string albumTag = "";  // Store full album tag for grouping
                    AlbumType albumType = AlbumType.AudienceRecording;

                    if (audioFiles.Length > 0)
                    {
                        try
                        {
                            using (var tagFile = TagLib.File.Create(audioFiles[0]))
                            {
                                albumTag = tagFile.Tag.Album ?? "";

                                // Check for Box Set format first: "Date - Venue - City, State: Box Set Name" (no space before colon)
                                var boxSetMatch = System.Text.RegularExpressions.Regex.Match(
                                    albumTag, @":\s*([^:]+)$");

                                // Check if there's a space before the colon (Official Release) or not (Box Set)
                                var spaceBeforeColonMatch = System.Text.RegularExpressions.Regex.Match(
                                    albumTag, @"\s:\s*(.+)$");

                                if (spaceBeforeColonMatch.Success)
                                {
                                    // Official Release format: "... : Release Name" (space before colon)
                                    officialRelease = spaceBeforeColonMatch.Groups[1].Value.Trim();
                                    albumType = AlbumType.AudienceRecording;
                                }
                                else if (boxSetMatch.Success)
                                {
                                    // Box Set format: "...: Box Set Name" (no space before colon)
                                    boxSetName = boxSetMatch.Groups[1].Value.Trim();
                                    albumType = AlbumType.OfficialRelease;
                                }
                            }
                        }
                        catch
                        {
                            // Ignore errors reading tags
                        }
                    }

                    _allShows.Add(new LibraryShow
                    {
                        Type = albumType,
                        Date = date,
                        Venue = venue,
                        City = city,
                        State = state,
                        Location = !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state)
                            ? $"{city}, {state}"
                            : city + state,
                        OfficialRelease = albumType == AlbumType.AudienceRecording ? officialRelease : boxSetName,
                        AlbumName = albumTag,  // Store full album tag for grouping
                        TrackCount = audioFiles.Length,
                        FolderPath = showFolder
                    });
                }
            }
        }
    }

    private void LoadOfficialReleases()
    {
        // First, scan for Studio Albums folder
        var studioAlbumsPath = Path.Combine(_librarySettings.OfficialReleasesPath, "Studio Albums");
        if (Directory.Exists(studioAlbumsPath))
        {
            var albumFolders = Directory.GetDirectories(studioAlbumsPath);

            foreach (var albumFolder in albumFolders)
            {
                var folderName = Path.GetFileName(albumFolder);
                var audioFiles = Directory.GetFiles(albumFolder, "*.flac")
                                    .Concat(Directory.GetFiles(albumFolder, "*.mp3"))
                                    .ToArray();

                // Expected format: "Album Name (Year)" or just "Album Name"
                string albumName = folderName;
                int? releaseYear = null;
                string edition = "";

                // Try to parse year from parentheses
                var yearMatch = System.Text.RegularExpressions.Regex.Match(
                    folderName, @"^(.+?)\s*\((\d{4})\)\s*$");

                if (yearMatch.Success)
                {
                    albumName = yearMatch.Groups[1].Value.Trim();
                    if (int.TryParse(yearMatch.Groups[2].Value, out var year))
                    {
                        releaseYear = year;
                    }
                }

                // Try to read Edition from first audio file's album tag
                if (audioFiles.Length > 0)
                {
                    try
                    {
                        using (var tagFile = TagLib.File.Create(audioFiles[0]))
                        {
                            var album = tagFile.Tag.Album ?? "";
                            // Check for edition in brackets like "Album Name (Year) [Edition]"
                            var editionMatch = System.Text.RegularExpressions.Regex.Match(
                                album, @"\[([^\]]+)\]\s*$");
                            if (editionMatch.Success)
                            {
                                edition = editionMatch.Groups[1].Value.Trim();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors reading tags
                    }
                }

                _allShows.Add(new LibraryShow
                {
                    Type = AlbumType.OfficialRelease,
                    AlbumName = albumName,
                    ReleaseYear = releaseYear,
                    Edition = edition,
                    TrackCount = audioFiles.Length,
                    FolderPath = albumFolder
                });
            }
        }

        // Then, scan for series folders (Dave's Picks, Road Trips, Dick's Picks, etc.)
        var seriesFolders = Directory.GetDirectories(_librarySettings.OfficialReleasesPath);

        foreach (var seriesFolder in seriesFolders)
        {
            var seriesFolderName = Path.GetFileName(seriesFolder);

            // Skip the Studio Albums folder - we already processed it above
            if (seriesFolderName.Equals("Studio Albums", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var releaseFolders = Directory.GetDirectories(seriesFolder);

            foreach (var releaseFolder in releaseFolders)
            {
                var folderName = Path.GetFileName(releaseFolder);
                var audioFiles = Directory.GetFiles(releaseFolder, "*.flac")
                                    .Concat(Directory.GetFiles(releaseFolder, "*.mp3"))
                                    .ToArray();

                if (audioFiles.Length == 0) continue;

                // Extract dates and venues from all tracks
                var dates = new HashSet<string>();
                var venues = new HashSet<string>();
                string officialRelease = "";
                string albumDate = "";
                string albumVenue = "";
                string albumCity = "";
                string albumState = "";
                bool albumParsed = false;

                foreach (var audioFile in audioFiles)
                {
                    try
                    {
                        using (var tagFile = TagLib.File.Create(audioFile))
                        {
                            var title = tagFile.Tag.Title ?? "";
                            var album = tagFile.Tag.Album ?? "";

                            // Parse Album tag on first file only
                            // Expected format: "Date - Venue - City, State - Release Name"
                            // Example: "1978-02-01 - Uptown Theatre - Chicago, IL - Dave's Picks Volume 57"
                            if (!albumParsed && !string.IsNullOrEmpty(album))
                            {
                                albumParsed = true;
                                var albumParts = album.Split(new[] { " - " }, StringSplitOptions.None);

                                if (albumParts.Length >= 4)
                                {
                                    // Parse structured album tag
                                    albumDate = albumParts[0].Trim();
                                    albumVenue = albumParts[1].Trim();
                                    var cityState = albumParts[2].Trim();

                                    // Parse "City, State" format
                                    var locationParts = cityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                                    albumCity = locationParts.Length > 0 ? locationParts[0].Trim() : "";
                                    albumState = locationParts.Length > 1 ? locationParts[1].Trim() : "";

                                    // Release name is everything after the third " - "
                                    officialRelease = string.Join(" - ", albumParts.Skip(3)).Trim();

                                    // Add to collections
                                    if (!string.IsNullOrEmpty(albumDate))
                                        dates.Add(albumDate);
                                    if (!string.IsNullOrEmpty(albumVenue))
                                        venues.Add(albumVenue);
                                }
                                else
                                {
                                    // Fallback: Try to extract release name from album tag
                                    var releaseMatch = System.Text.RegularExpressions.Regex.Match(
                                        album,
                                        @"((?:Dave's Picks|Dick's Picks|Road Trips|Download Series)\s*,?\s*Vol(?:ume|\.)?\s+\d+(?:\s+No\.\s+\d+)?)",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (releaseMatch.Success)
                                    {
                                        officialRelease = releaseMatch.Groups[1].Value.Trim();
                                    }
                                }
                            }

                            // Extract date from title (various formats)
                            var dateMatch = System.Text.RegularExpressions.Regex.Match(
                                title, @"(\d{4}-\d{2}-\d{2})");
                            if (dateMatch.Success)
                            {
                                dates.Add(dateMatch.Groups[1].Value);
                            }

                            // Extract venue from title patterns like:
                            // "Song (Live at Venue, City, State, Date)"
                            // "Song (Filler: Date - Venue, City, State)"
                            var venueMatch = System.Text.RegularExpressions.Regex.Match(
                                title, @"Live (?:at|in) ([^,]+),");
                            if (venueMatch.Success)
                            {
                                venues.Add(venueMatch.Groups[1].Value.Trim());
                            }
                            else
                            {
                                // Try "Filler: Date - Venue, ..." pattern
                                venueMatch = System.Text.RegularExpressions.Regex.Match(
                                    title, @"Filler:\s*\d{4}-\d{2}-\d{2}\s*-\s*([^,]+),");
                                if (venueMatch.Success)
                                {
                                    venues.Add(venueMatch.Groups[1].Value.Trim());
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors reading individual files
                    }
                }

                // Use folder name as fallback for official release
                if (string.IsNullOrEmpty(officialRelease))
                {
                    officialRelease = folderName;
                }

                // Build Location string from parsed city/state
                var location = "";
                if (!string.IsNullOrEmpty(albumCity) && !string.IsNullOrEmpty(albumState))
                {
                    location = $"{albumCity}, {albumState}";
                }
                else if (!string.IsNullOrEmpty(albumCity))
                {
                    location = albumCity;
                }
                else if (!string.IsNullOrEmpty(albumState))
                {
                    location = albumState;
                }

                // Create LibraryShow entry
                _allShows.Add(new LibraryShow
                {
                    Type = AlbumType.OfficialRelease,
                    OfficialRelease = officialRelease,
                    Date = !string.IsNullOrEmpty(albumDate) ? albumDate : (dates.Count > 0 ? dates.OrderBy(d => d).First() : ""),
                    ContainsDates = dates.OrderBy(d => d).ToList(),
                    ContainsVenues = venues.OrderBy(v => v).ToList(),
                    TrackCount = audioFiles.Length,
                    FolderPath = releaseFolder,
                    // Use parsed values from Album tag
                    Venue = !string.IsNullOrEmpty(albumVenue) ? albumVenue : (venues.FirstOrDefault() ?? ""),
                    City = albumCity,
                    State = albumState,
                    Location = !string.IsNullOrEmpty(location) ? location : (venues.Count > 0 ? string.Join(", ", venues) : "")
                });
            }
        }
    }

    private void GroupMultiFolderAlbums()
    {
        // Group folders by Album tag within the same AlbumType
        // Multiple folders with the same Album tag should be merged into a single LibraryShow

        var groupedShows = new List<LibraryShow>();

        // Group by AlbumType first, then by AlbumName (Album tag)
        var groups = _allShows
            .GroupBy(show => new { show.Type, show.AlbumName })
            .Where(g => !string.IsNullOrEmpty(g.Key.AlbumName));  // Only group shows that have an Album tag

        // Process groups with matching Album tags
        foreach (var group in groups)
        {
            if (group.Count() == 1)
            {
                // Single folder - no grouping needed, add as-is
                groupedShows.Add(group.First());
            }
            else
            {
                // Multiple folders with same Album tag - merge them
                var firstShow = group.First();
                var mergedShow = new LibraryShow
                {
                    Type = firstShow.Type,
                    AlbumName = firstShow.AlbumName,
                    OfficialRelease = firstShow.OfficialRelease,
                    Edition = firstShow.Edition,
                    ReleaseYear = firstShow.ReleaseYear,

                    // Aggregate dates and venues from all folders
                    ContainsDates = group.SelectMany(s => string.IsNullOrEmpty(s.Date)
                        ? Enumerable.Empty<string>()
                        : new[] { s.Date })
                        .Distinct()
                        .OrderBy(d => d)
                        .ToList(),

                    ContainsVenues = group.SelectMany(s => string.IsNullOrEmpty(s.Venue)
                        ? Enumerable.Empty<string>()
                        : new[] { s.Venue })
                        .Distinct()
                        .OrderBy(v => v)
                        .ToList(),

                    // Use first show's metadata for display (or aggregate if multi-date)
                    Date = firstShow.Date,
                    Venue = firstShow.Venue,
                    City = firstShow.City,
                    State = firstShow.State,
                    Location = firstShow.Location,

                    // Sum track counts from all folders
                    TrackCount = group.Sum(s => s.TrackCount),

                    // Collect all folder paths
                    FolderPaths = group.SelectMany(s => s.FolderPaths).Distinct().ToList()
                };

                groupedShows.Add(mergedShow);
            }
        }

        // Add shows without Album tags (ungrouped)
        var showsWithoutAlbumTag = _allShows.Where(s => string.IsNullOrEmpty(s.AlbumName));
        groupedShows.AddRange(showsWithoutAlbumTag);

        // Replace _allShows with grouped shows
        _allShows = groupedShows;
    }

    private List<ConcertDate> LoadConcertDates()
    {
        var concertDates = new List<ConcertDate>();

        // Iterate through all shows and extract unique concert dates
        foreach (var show in _allShows)
        {
            // For Official Releases and Box Sets, we need to read tracks to get authoritative dates
            // For Audience Recordings, we can use the folder-based date
            if (show.Type == AlbumType.OfficialRelease)
            {
                // Read all tracks from all folders to extract unique concert dates from TITLE tags
                var dateTrackMap = new Dictionary<string, List<TrackInfo>>();

                foreach (var folderPath in show.FolderPaths)
                {
                    if (!Directory.Exists(folderPath)) continue;

                    var tracks = _metadataService.ReadFolder(folderPath);
                    foreach (var track in tracks)
                    {
                        // Extract date from track.TrackDate (populated by ReadFolder from TITLE tag)
                        if (!string.IsNullOrEmpty(track.TrackDate))
                        {
                            if (!dateTrackMap.ContainsKey(track.TrackDate))
                            {
                                dateTrackMap[track.TrackDate] = new List<TrackInfo>();
                            }
                            dateTrackMap[track.TrackDate].Add(track);
                        }
                    }
                }

                // Create a ConcertDate entry for each unique date found in tracks
                foreach (var kvp in dateTrackMap)
                {
                    var date = kvp.Key;
                    var tracksForDate = kvp.Value;

                    // Determine collection name
                    string collectionName = "";
                    if (!string.IsNullOrEmpty(show.AlbumName))
                    {
                        collectionName = show.AlbumName;
                    }
                    else if (!string.IsNullOrEmpty(show.OfficialRelease))
                    {
                        collectionName = show.OfficialRelease;
                    }
                    else
                    {
                        collectionName = "Official Release";
                    }

                    // Extract venue and location from first track for this date (if available)
                    // Tracks might have venue info embedded, but for official releases this is rare
                    // Fall back to show-level venue/location
                    string venue = show.Venue;
                    string location = show.Location;

                    // For multi-folder albums, find folders that contain tracks for this date
                    List<string> dateFolderPaths = new List<string>();
                    foreach (var folderPath in show.FolderPaths)
                    {
                        // Check if any tracks in this folder match this date
                        if (tracksForDate.Any(t => t.FilePath.StartsWith(folderPath)))
                        {
                            dateFolderPaths.Add(folderPath);
                        }
                    }

                    // If no specific folders found, use all folders from the show
                    if (dateFolderPaths.Count == 0)
                    {
                        dateFolderPaths.AddRange(show.FolderPaths);
                    }

                    concertDates.Add(new ConcertDate
                    {
                        Date = date,
                        Venue = venue,
                        Location = location,
                        CollectionName = collectionName,
                        TrackCount = tracksForDate.Count, // Accurate count for this date only
                        SourceShow = show,
                        FolderPaths = dateFolderPaths
                    });
                }
            }
            else
            {
                // Audience Recording - use folder-based date (from folder name parsing)
                List<string> dates = new List<string>();

                if (!string.IsNullOrEmpty(show.Date))
                {
                    dates.Add(show.Date);
                }

                // Create a ConcertDate entry for each unique date
                foreach (var date in dates)
                {
                    // Determine collection name
                    string collectionName = "";
                    if (!string.IsNullOrEmpty(show.AlbumName))
                    {
                        collectionName = show.AlbumName;
                    }
                    else if (!string.IsNullOrEmpty(show.OfficialRelease))
                    {
                        collectionName = show.OfficialRelease;
                    }
                    else
                    {
                        collectionName = "Audience Recording";
                    }

                    // For multi-folder albums, find the specific folder matching this date
                    List<string> dateFolderPaths = new List<string>();
                    if (show.FolderPaths.Count > 1)
                    {
                        // Multi-folder album - find folder(s) matching this date
                        foreach (var folderPath in show.FolderPaths)
                        {
                            if (folderPath.Contains(date))
                            {
                                dateFolderPaths.Add(folderPath);
                            }
                        }
                    }

                    // If no specific folders found, use all folders from the show
                    if (dateFolderPaths.Count == 0)
                    {
                        dateFolderPaths.AddRange(show.FolderPaths);
                    }

                    // Count tracks in the matched folders for accurate per-date count
                    int trackCount = 0;
                    foreach (var folderPath in dateFolderPaths)
                    {
                        if (Directory.Exists(folderPath))
                        {
                            var audioFiles = Directory.GetFiles(folderPath, "*.flac")
                                                .Concat(Directory.GetFiles(folderPath, "*.mp3"))
                                                .ToArray();
                            trackCount += audioFiles.Length;
                        }
                    }

                    concertDates.Add(new ConcertDate
                    {
                        Date = date,
                        Venue = show.Venue,
                        Location = show.Location,
                        CollectionName = collectionName,
                        TrackCount = trackCount, // Accurate count for this date only
                        SourceShow = show,
                        FolderPaths = dateFolderPaths
                    });
                }
            }
        }

        // Sort by date ascending
        return concertDates.OrderBy(cd => cd.Date).ToList();
    }

    private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShowsDataGrid.SelectedItem is LibraryShow show)
        {
            OpenConcertView(show);
        }
        else if (ShowsDataGrid.SelectedItem is ConcertDate concertDate)
        {
            // Open concert view filtered to this specific date
            OpenConcertDateView(concertDate);
        }
    }

    private void OpenConcertView(LibraryShow show)
    {
        try
        {
            // Save reference to current show
            _currentShow = show;

            // Load info based on album type
            if (show.Type == AlbumType.OfficialRelease)
            {
                // Official Release - show album/release name in AlbumNameText (always visible)
                AlbumNameText.Text = !string.IsNullOrEmpty(show.OfficialRelease) ? show.OfficialRelease : show.AlbumName;

                // VenueText is used for single-night shows only (will be hidden for multi-night)
                VenueText.Text = "";

                // Show date (single-date releases) or multiple dates
                if (!string.IsNullOrEmpty(show.Date))
                {
                    DateText.Text = show.Date;
                }
                else if (show.ContainsDates.Count > 1)
                {
                    DateText.Text = string.Join(" & ", show.ContainsDates);
                }
                else if (show.ReleaseYear.HasValue)
                {
                    DateText.Text = $"Released {show.ReleaseYear.Value}";
                }
                else
                {
                    DateText.Text = "";
                }

                // Show venue (single-venue releases) or multiple venues
                if (!string.IsNullOrEmpty(show.Venue))
                {
                    LocationText.Text = show.Venue;
                }
                else if (show.ContainsVenues.Count > 0)
                {
                    LocationText.Text = string.Join(", ", show.ContainsVenues);
                }
                else
                {
                    LocationText.Text = "";
                }

                // Show City, State location or Edition in BoxSetText (will be hidden for multi-night)
                if (!string.IsNullOrEmpty(show.Location))
                {
                    BoxSetText.Text = show.Location;
                    BoxSetText.Visibility = Visibility.Visible;
                }
                else if (!string.IsNullOrEmpty(show.Edition))
                {
                    BoxSetText.Text = $"Edition: {show.Edition}";
                    BoxSetText.Visibility = Visibility.Visible;
                }
                else
                {
                    BoxSetText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // Audience Recording - AlbumNameText is typically empty for audience recordings
                AlbumNameText.Text = "";

                // Show venue, location, and date
                VenueText.Text = show.Venue;
                LocationText.Text = show.Location;
                DateText.Text = show.Date;

                // Show official release name if this is an official release
                if (!string.IsNullOrEmpty(show.OfficialRelease))
                {
                    BoxSetText.Text = $"Release: {show.OfficialRelease}";
                    BoxSetText.Visibility = Visibility.Visible;
                }
                else
                {
                    BoxSetText.Visibility = Visibility.Collapsed;
                }
            }

            // Load artwork - try file first, then embedded in FLAC
            var artworkPath = Path.Combine(show.FolderPath, "cover.jpg");
            if (!File.Exists(artworkPath))
            {
                artworkPath = Path.Combine(show.FolderPath, "folder.jpg");
            }

            System.Windows.Media.Imaging.BitmapImage? bitmap = null;

            if (File.Exists(artworkPath))
            {
                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                bitmap.EndInit();
            }
            else
            {
                // Try to extract embedded artwork from first audio file
                var audioFiles = Directory.GetFiles(show.FolderPath, "*.flac")
                                    .Concat(Directory.GetFiles(show.FolderPath, "*.mp3"))
                                    .ToArray();
                if (audioFiles.Length > 0)
                {
                    try
                    {
                        var tagFile = TagLib.File.Create(audioFiles[0]);
                        if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                        {
                            var picture = tagFile.Tag.Pictures[0];
                            using (var ms = new System.IO.MemoryStream(picture.Data.Data))
                            {
                                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = ms;
                                bitmap.EndInit();
                                bitmap.Freeze(); // Important for cross-thread access
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors extracting artwork
                    }
                }
            }

            if (bitmap != null)
            {
                AlbumArtwork.Source = bitmap;
                ArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArtwork.Source = null;
                ArtworkPlaceholder.Visibility = Visibility.Visible;
            }

            // Load tracks from ALL folders (multi-folder album support)
            _currentTracks = new List<TrackInfo>();
            foreach (var folderPath in show.FolderPaths)
            {
                var folderTracks = _metadataService.ReadFolder(folderPath);
                _currentTracks.AddRange(folderTracks);
            }

            // Detect multi-night albums by checking for multiple unique TrackDate values
            var distinctDates = _currentTracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .Select(t => t.TrackDate)
                .Distinct()
                .Count();

            // Sort tracks:
            // - Multi-night albums (2+ distinct dates): Sort by date first, then disc/track
            // - Single-night albums (0 or 1 date): Sort by disc/track only
            if (distinctDates > 1)
            {
                // Multi-night: Sort by date first (yyyy-MM-dd sorts correctly as string),
                // then disc number, then track number
                _currentTracks = _currentTracks
                    .OrderBy(t => t.TrackDate ?? "")
                    .ThenBy(t => t.DiscNumber)
                    .ThenBy(t => t.TrackNumber)
                    .ToList();
            }
            else
            {
                // Single-night: Sort by disc number and track number only
                _currentTracks = _currentTracks
                    .OrderBy(t => t.DiscNumber)
                    .ThenBy(t => t.TrackNumber)
                    .ToList();
            }

            // Populate TrackDate for tracks that don't have embedded dates
            // This ensures DisplayTitle shows "Song (yyyy-MM-dd)" format
            foreach (var track in _currentTracks)
            {
                // If track has no date, inherit from album
                if (string.IsNullOrEmpty(track.TrackDate) && !string.IsNullOrEmpty(show.Date))
                {
                    track.TrackDate = show.Date;
                }
            }

            // Build date-venue map for venue/location lookup
            _dateVenueMap = BuildDateVenueMapFromTracks(_currentTracks, show);

            // Populate concert dates list for Jump To Date links
            _concertDates.Clear();
            var distinctDatesOrdered = _currentTracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .Select(t => t.TrackDate)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var date in distinctDatesOrdered)
            {
                _concertDates.Add(date);
            }

            // Build concert view items (flat list with headers and tracks)
            if (_concertDates.Count > 1)
            {
                // Multi-night: Build collapsible sections
                BuildCollapsibleConcertView();
                TracksDataGrid.ItemsSource = _concertViewItems;
            }
            else
            {
                // Single-night: Bind directly to tracks (no headers)
                TracksDataGrid.ItemsSource = _currentTracks;
            }

            TrackCountText.Text = $"{_currentTracks.Count} tracks";

            // Show/hide Jump to Date dropdown and expand/collapse buttons based on whether this is a multi-night show
            // For multi-night shows, also hide venue/date/location (shown in section headers instead)
            if (_concertDates.Count > 1)
            {
                // Multi-night: Show Jump to Date dropdown and expand/collapse buttons, hide venue/date/location/boxset
                JumpToDatePanel.Visibility = Visibility.Visible;
                ExpandCollapseButtons.Visibility = Visibility.Visible;
                VenueText.Visibility = Visibility.Collapsed;
                DateText.Visibility = Visibility.Collapsed;
                LocationText.Visibility = Visibility.Collapsed;
                BoxSetText.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Single-night: Hide Jump to Date dropdown and expand/collapse buttons, show venue/date/location
                JumpToDatePanel.Visibility = Visibility.Collapsed;
                ExpandCollapseButtons.Visibility = Visibility.Collapsed;
                VenueText.Visibility = Visibility.Visible;
                DateText.Visibility = Visibility.Visible;
                LocationText.Visibility = Visibility.Visible;
                // BoxSetText visibility is already set above based on whether Location or Edition exists
            }

            // Set album type label
            AlbumTypeText.Text = show.Type == AlbumType.OfficialRelease ? "Official Release" : "Audience Recording";

            // Switch views
            LibraryView.Visibility = Visibility.Collapsed;
            ConcertView.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            // Update window title
            Title = $"{show.Date} - {show.Venue}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading concert: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Public method to navigate to a track's parent concert from external windows (e.g., PlaylistWindow).
    /// Finds the concert containing the track, opens it, and scrolls to the specific track.
    /// </summary>
    /// <param name="track">Track to navigate to</param>
    /// <returns>True if concert was found and navigation succeeded, false otherwise</returns>
    public bool NavigateToTrack(TrackInfo track)
    {
        if (track == null || string.IsNullOrEmpty(track.FilePath))
            return false;

        // Extract parent folder from file path
        var trackFolder = Path.GetDirectoryName(track.FilePath);
        if (string.IsNullOrEmpty(trackFolder))
            return false;

        // Find the show that contains this folder path
        var parentShow = _allShows.FirstOrDefault(show =>
            show.FolderPaths.Any(folderPath =>
                folderPath.Equals(trackFolder, StringComparison.OrdinalIgnoreCase)));

        if (parentShow == null)
            return false;

        // Open the concert view
        OpenConcertView(parentShow);

        // Scroll to and highlight the track
        Dispatcher.InvokeAsync(() =>
        {
            // Find the track in the current view
            TrackInfo? targetTrack = null;

            if (_concertDates.Count > 1)
            {
                // Multi-night view: Find track in _concertViewItems
                var trackItem = _concertViewItems.OfType<TrackViewItem>()
                    .FirstOrDefault(t => t.Track.FilePath == track.FilePath);

                if (trackItem != null)
                {
                    TracksDataGrid.SelectedItem = trackItem;
                    TracksDataGrid.ScrollIntoView(trackItem);
                }
            }
            else
            {
                // Single-night view: Find track in _currentTracks
                targetTrack = _currentTracks.FirstOrDefault(t => t.FilePath == track.FilePath);

                if (targetTrack != null)
                {
                    TracksDataGrid.SelectedItem = targetTrack;
                    TracksDataGrid.ScrollIntoView(targetTrack);
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        // Bring window to foreground
        Activate();
        Focus();

        return true;
    }

    /// <summary>
    /// Builds a lookup dictionary mapping concert dates to their venue and location.
    /// For box sets and multi-night shows, extracts venue/location from folder paths matching each date.
    /// </summary>
    /// <param name="tracks">List of tracks with TrackDate populated</param>
    /// <param name="show">The LibraryShow containing folder path(s)</param>
    /// <returns>Dictionary keyed by yyyy-MM-dd date with (venue, location) tuples</returns>
    private Dictionary<string, (string venue, string location)> BuildDateVenueMapFromTracks(List<TrackInfo> tracks, LibraryShow show)
    {
        var map = new Dictionary<string, (string venue, string location)>();

        System.Diagnostics.Debug.WriteLine($"BuildDateVenueMapFromTracks: Building map for {show.FolderPaths.Count} folder(s)");

        // Get unique dates from tracks
        var uniqueDates = tracks
            .Where(t => !string.IsNullOrEmpty(t.TrackDate))
            .Select(t => t.TrackDate)
            .Distinct()
            .ToList();

        foreach (var date in uniqueDates)
        {
            // Find tracks for this date
            var dateTrack = tracks.FirstOrDefault(t => t.TrackDate == date);
            if (dateTrack == null) continue;

            // Find the folder path containing this track
            var trackFolderPath = show.FolderPaths.FirstOrDefault(fp =>
                dateTrack.FilePath.StartsWith(fp, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(trackFolderPath) || !Directory.Exists(trackFolderPath))
            {
                System.Diagnostics.Debug.WriteLine($"  Date '{date}': No matching folder found");
                continue;
            }

            var folderName = Path.GetFileName(trackFolderPath);
            System.Diagnostics.Debug.WriteLine($"  Date '{date}': Processing folder '{folderName}'");

            // Try parsing "yyyy-MM-dd - Venue, City, State" format (audience recordings)
            var parts = folderName.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length >= 2 && parts[0].Trim() == date)
            {
                // Folder name format: "yyyy-MM-dd - Venue, City, State"
                var remainder = string.Join(" - ", parts.Skip(1));  // "Venue, City, State" or "Venue - City, State"

                // Parse venue and location
                string venue = "";
                string city = "";
                string state = "";
                string location = "";

                // Try "Venue, City, State" format first
                var locationParts = remainder.Split(',');
                if (locationParts.Length >= 2)
                {
                    venue = locationParts[0].Trim();
                    city = locationParts.Length > 1 ? locationParts[1].Trim() : "";
                    state = locationParts.Length > 2 ? locationParts[2].Trim() : "";

                    if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state))
                    {
                        location = $"{city}, {state}";
                    }
                    else if (!string.IsNullOrEmpty(city))
                    {
                        location = city;
                    }
                }
                else
                {
                    // Try "Venue - City, State" format (alternative)
                    var altParts = remainder.Split(" - ");
                    if (altParts.Length >= 2)
                    {
                        venue = altParts[0].Trim();
                        var locationStr = altParts[1].Trim();
                        var locParts = locationStr.Split(',');
                        city = locParts.Length > 0 ? locParts[0].Trim() : "";
                        state = locParts.Length > 1 ? locParts[1].Trim() : "";

                        if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state))
                        {
                            location = $"{city}, {state}";
                        }
                        else
                        {
                            location = locationStr;
                        }
                    }
                    else
                    {
                        venue = remainder;
                        location = "";
                    }
                }

                // Add to map
                map[date] = (venue, location);
                System.Diagnostics.Debug.WriteLine($"    Added from folder: '{date}' → '{venue}', '{location}'");
            }
            else
            {
                // Box set or official release folder - no date/venue in folder name
                // Use show-level metadata as fallback
                var venue = show.Venue ?? "";
                var location = show.Location ?? "";
                map[date] = (venue, location);
                System.Diagnostics.Debug.WriteLine($"    Added from show metadata: '{date}' → '{venue}', '{location}'");
            }
        }

        System.Diagnostics.Debug.WriteLine($"BuildDateVenueMapFromTracks: Final map has {map.Count} entries");
        return map;
    }

    /// <summary>
    /// Builds a flat list of concert view items with collapsible date sections.
    /// First section is expanded, others are collapsed.
    /// </summary>
    private void BuildCollapsibleConcertView()
    {
        _concertViewItems.Clear();

        // Group tracks by date
        var tracksByDate = _currentTracks
            .Where(t => !string.IsNullOrEmpty(t.TrackDate))
            .GroupBy(t => t.TrackDate)
            .OrderBy(g => g.Key);

        bool isFirstSection = true;

        foreach (var dateGroup in tracksByDate)
        {
            var date = dateGroup.Key;
            var dateTracks = dateGroup.ToList();

            // Get venue and location for this date
            var (venue, location) = _dateVenueMap.TryGetValue(date, out var venueInfo)
                ? venueInfo
                : ("", "");

            // Create date header
            var header = new DateHeaderItem
            {
                Date = date,
                Venue = venue,
                Location = location,
                TrackCount = dateTracks.Count,
                IsExpanded = isFirstSection  // First section expanded, others collapsed
            };

            _concertViewItems.Add(header);

            // Add tracks if this section is expanded
            if (header.IsExpanded)
            {
                foreach (var track in dateTracks)
                {
                    _concertViewItems.Add(new TrackViewItem
                    {
                        Track = track,
                        ParentHeader = header
                    });
                }
            }

            isFirstSection = false;
        }

        System.Diagnostics.Debug.WriteLine($"BuildCollapsibleConcertView: Created {_concertViewItems.Count} view items");
    }

    /// <summary>
    /// Toggles expansion of a date section header.
    /// When expanding, collapses all other sections (single selection).
    /// </summary>
    private void ToggleDateSection(DateHeaderItem header)
    {
        if (header.IsExpanded)
        {
            // Collapse this header
            var tracksToRemove = _concertViewItems
                .OfType<TrackViewItem>()
                .Where(t => t.ParentHeader == header)
                .ToList();

            foreach (var track in tracksToRemove)
            {
                _concertViewItems.Remove(track);
            }

            header.IsExpanded = false;
            SelectedDate = "";
        }
        else
        {
            // Collapse ALL other sections first (single selection behavior)
            var allHeaders = _concertViewItems.OfType<DateHeaderItem>().ToList();
            foreach (var otherHeader in allHeaders)
            {
                if (otherHeader != header && otherHeader.IsExpanded)
                {
                    var tracksToRemove = _concertViewItems
                        .OfType<TrackViewItem>()
                        .Where(t => t.ParentHeader == otherHeader)
                        .ToList();

                    foreach (var track in tracksToRemove)
                    {
                        _concertViewItems.Remove(track);
                    }

                    otherHeader.IsExpanded = false;
                }
            }

            // Now expand this header
            var headerIndex = _concertViewItems.IndexOf(header);
            var dateTracks = _currentTracks
                .Where(t => t.TrackDate == header.Date)
                .ToList();

            int insertIndex = headerIndex + 1;
            foreach (var track in dateTracks)
            {
                _concertViewItems.Insert(insertIndex, new TrackViewItem
                {
                    Track = track,
                    ParentHeader = header
                });
                insertIndex++;
            }

            header.IsExpanded = true;
            SelectedDate = header.Date;
        }
    }

    /// <summary>
    /// Expands all date sections, showing all tracks.
    /// </summary>
    private void ExpandAllSections()
    {
        var allHeaders = _concertViewItems.OfType<DateHeaderItem>().ToList();
        foreach (var header in allHeaders)
        {
            if (!header.IsExpanded)
            {
                var headerIndex = _concertViewItems.IndexOf(header);
                var dateTracks = _currentTracks
                    .Where(t => t.TrackDate == header.Date)
                    .ToList();

                int insertIndex = headerIndex + 1;
                foreach (var track in dateTracks)
                {
                    _concertViewItems.Insert(insertIndex, new TrackViewItem
                    {
                        Track = track,
                        ParentHeader = header
                    });
                    insertIndex++;
                }

                header.IsExpanded = true;
            }
        }

        // Keep the last expanded section as selected
        SelectedDate = allHeaders.LastOrDefault()?.Date ?? "";
    }

    /// <summary>
    /// Collapses all date sections, hiding all tracks.
    /// </summary>
    private void CollapseAllSections()
    {
        var allHeaders = _concertViewItems.OfType<DateHeaderItem>().ToList();
        foreach (var header in allHeaders)
        {
            if (header.IsExpanded)
            {
                var tracksToRemove = _concertViewItems
                    .OfType<TrackViewItem>()
                    .Where(t => t.ParentHeader == header)
                    .ToList();

                foreach (var track in tracksToRemove)
                {
                    _concertViewItems.Remove(track);
                }

                header.IsExpanded = false;
            }
        }

        SelectedDate = "";
    }

    /// <summary>
    /// Scrolls to and expands the section for the specified date.
    /// Called when user clicks a date link in the left panel.
    /// Collapses all other sections and scrolls the target header to the top.
    /// </summary>
    private async void ScrollToDate(string date)
    {
        if (string.IsNullOrEmpty(date)) return;

        // Find the date header for this date
        var targetHeader = _concertViewItems
            .OfType<DateHeaderItem>()
            .FirstOrDefault(h => h.Date == date);

        if (targetHeader == null) return;

        // Step 1: Collapse all sections except the target
        var allHeaders = _concertViewItems.OfType<DateHeaderItem>().ToList();
        foreach (var header in allHeaders)
        {
            if (header != targetHeader && header.IsExpanded)
            {
                // Collapse this section by removing its tracks
                var tracksToRemove = _concertViewItems
                    .OfType<TrackViewItem>()
                    .Where(t => t.ParentHeader == header)
                    .ToList();

                foreach (var track in tracksToRemove)
                {
                    _concertViewItems.Remove(track);
                }

                header.IsExpanded = false;
            }
        }

        // Step 2: Expand target section if collapsed
        if (!targetHeader.IsExpanded)
        {
            var headerIndex = _concertViewItems.IndexOf(targetHeader);
            if (headerIndex >= 0 && _currentTracks.Count > 0)
            {
                var dateTracks = _currentTracks.Where(t => t.TrackDate == targetHeader.Date).ToList();

                int insertIndex = headerIndex + 1;
                foreach (var track in dateTracks)
                {
                    _concertViewItems.Insert(insertIndex++, new TrackViewItem
                    {
                        Track = track,
                        ParentHeader = targetHeader
                    });
                }

                targetHeader.IsExpanded = true;
            }
        }

        // Step 3: Update selected date for UI highlighting
        SelectedDate = date;

        // Step 4: Wait for layout to complete, then scroll header into view
        await Dispatcher.InvokeAsync(() =>
        {
            TracksDataGrid.ScrollIntoView(targetHeader);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }


    private void OpenConcertDateView(ConcertDate concertDate)
    {
        try
        {
            // Display date, venue, location, and collection name
            VenueText.Text = concertDate.Venue;
            LocationText.Text = concertDate.Location;
            DateText.Text = concertDate.Date;

            // Show collection name (box set or release name)
            if (!string.IsNullOrEmpty(concertDate.CollectionName))
            {
                BoxSetText.Text = $"Collection: {concertDate.CollectionName}";
                BoxSetText.Visibility = Visibility.Visible;
            }
            else
            {
                BoxSetText.Visibility = Visibility.Collapsed;
            }

            // Load artwork from the first folder path (all folders for same concert likely have same artwork)
            var artworkPath = "";
            if (concertDate.FolderPaths.Count > 0)
            {
                artworkPath = Path.Combine(concertDate.FolderPaths[0], "cover.jpg");
                if (!File.Exists(artworkPath))
                {
                    artworkPath = Path.Combine(concertDate.FolderPaths[0], "folder.jpg");
                }
            }

            System.Windows.Media.Imaging.BitmapImage? bitmap = null;

            if (File.Exists(artworkPath))
            {
                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                bitmap.EndInit();
            }
            else if (concertDate.FolderPaths.Count > 0)
            {
                // Try to extract embedded artwork from first audio file
                var audioFiles = Directory.GetFiles(concertDate.FolderPaths[0], "*.flac")
                                    .Concat(Directory.GetFiles(concertDate.FolderPaths[0], "*.mp3"))
                                    .ToArray();
                if (audioFiles.Length > 0)
                {
                    try
                    {
                        var tagFile = TagLib.File.Create(audioFiles[0]);
                        if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                        {
                            var picture = tagFile.Tag.Pictures[0];
                            using (var ms = new System.IO.MemoryStream(picture.Data.Data))
                            {
                                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = ms;
                                bitmap.EndInit();
                                bitmap.Freeze(); // Important for cross-thread access
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors extracting artwork
                    }
                }
            }

            if (bitmap != null)
            {
                AlbumArtwork.Source = bitmap;
                ArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArtwork.Source = null;
                ArtworkPlaceholder.Visibility = Visibility.Visible;
            }

            // Load tracks from ALL folder paths associated with this specific date
            _currentTracks = new List<TrackInfo>();
            foreach (var folderPath in concertDate.FolderPaths)
            {
                var folderTracks = _metadataService.ReadFolder(folderPath);
                _currentTracks.AddRange(folderTracks);
            }

            // Sort tracks by disc number and track number (disc-aware: 101, 102... 201, 202...)
            _currentTracks = _currentTracks
                .OrderBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .ToList();

            // Populate TrackDate for tracks that don't have embedded dates
            // This ensures DisplayTitle shows "Song (yyyy-MM-dd)" format
            foreach (var track in _currentTracks)
            {
                // If track has no date, inherit from concert date
                if (string.IsNullOrEmpty(track.TrackDate) && !string.IsNullOrEmpty(concertDate.Date))
                {
                    track.TrackDate = concertDate.Date;
                }
            }

            TracksDataGrid.ItemsSource = _currentTracks;
            TrackCountText.Text = $"{_currentTracks.Count} tracks";

            // Set album type label - concert dates are always from audience recordings or official releases
            AlbumTypeText.Text = "Concert Date View";

            // Switch views
            LibraryView.Visibility = Visibility.Collapsed;
            ConcertView.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            // Update window title
            Title = $"{concertDate.Date} - {concertDate.Venue}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading concert date: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // Don't stop playback - let it continue playing
        // Don't clear _updateTimer - let it keep updating

        // Clear concert view data
        TracksDataGrid.ItemsSource = null;
        AlbumArtwork.Source = null;

        // Switch views (but keep player controls visible)
        ConcertView.Visibility = Visibility.Collapsed;
        LibraryView.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        // PlayerControls stays visible

        // Reset window title
        Title = "DeadEditor - Library";
    }

    private void TracksDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.MouseDoubleClick += TracksDataGridRow_MouseDoubleClick;
    }

    private void TracksDataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.Item is TrackInfo track)
        {
            // Load playlist and play selected track using singleton
            _audioPlayer.LoadPlaylist(_currentTracks);
            _audioPlayer.Play(track);
        }
    }

    /// <summary>
    /// Helper method to add tracks to the playlist, avoiding duplicates.
    /// Uses TrackInfo.Equals() which compares FilePath for duplicate detection.
    /// </summary>
    private void AddTracksToPlaylist(System.Collections.Generic.IEnumerable<TrackInfo> tracks)
    {
        foreach (var track in tracks)
        {
            // FilePath equality already handled by TrackInfo.Equals()
            if (!App.PlaybackService.Playlist.Contains(track))
                App.PlaybackService.Playlist.Add(track);
        }
    }

    /// <summary>
    /// Gets the forward segue chain starting from the clicked track.
    /// Walks forward through consecutive segue tracks until a track does NOT segue.
    /// Example: "China Cat Sunflower >" → returns ["China Cat >", "I Know You Rider"]
    /// Example: "Dark Star" (no segue) → returns ["Dark Star"]
    /// </summary>
    /// <param name="startTrack">The track that was clicked</param>
    /// <param name="allTracks">All tracks for the current concert (in playback order)</param>
    /// <returns>List containing startTrack + any forward segue chain</returns>
    private List<TrackInfo> GetForwardSegueChain(TrackInfo startTrack, List<TrackInfo> allTracks)
    {
        var chain = new List<TrackInfo>();

        // Find the index of the start track
        var startIndex = allTracks.IndexOf(startTrack);
        if (startIndex == -1)
        {
            // Track not found - return empty list
            return chain;
        }

        // Walk forward from startIndex
        for (int i = startIndex; i < allTracks.Count; i++)
        {
            var currentTrack = allTracks[i];
            chain.Add(currentTrack);

            // Stop if this track does NOT segue
            if (!currentTrack.Segue)
                break;

            // If this is the last track, stop even if it has segue marker
            if (i == allTracks.Count - 1)
                break;
        }

        return chain;
    }

    /// <summary>
    /// Double-click handler: Adds track + forward segue chain to playlist, then plays it.
    /// </summary>
    private void TracksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Get the track from the clicked row (handles both single-night and multi-night views)
        var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);
        if (track != null)
        {
            // Get the forward segue chain (track + any tracks it segues into)
            var segueChain = GetForwardSegueChain(track, _currentTracks);

            // Add the entire chain to playlist
            AddTracksToPlaylist(segueChain);

            // Play the clicked track (first in chain)
            App.PlaybackService.Play(track);
        }
    }

    /// <summary>
    /// Context menu: Play Now - Adds track to playlist and starts playing.
    /// </summary>
    private void PlayNowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);
        if (track != null)
        {
            // Get the forward segue chain (track + any tracks it segues into)
            var segueChain = GetForwardSegueChain(track, _currentTracks);
            AddTracksToPlaylist(segueChain);
            App.PlaybackService.Play(track);
        }
    }

    /// <summary>
    /// Context menu: Add to Playlist - Adds track without starting playback.
    /// </summary>
    private void AddToPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);
        if (track != null)
        {
            // Get the forward segue chain (track + any tracks it segues into)
            var segueChain = GetForwardSegueChain(track, _currentTracks);
            AddTracksToPlaylist(segueChain);
        }
    }

    /// <summary>
    /// Context menu: Remove from Playlist - Removes the selected track from playlist.
    /// </summary>
    private void RemoveFromPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);
        if (track != null)
        {
            App.PlaybackService.Playlist.Remove(track);
        }
    }

    /// <summary>
    /// Context menu: Add Selected to Playlist - Adds all selected tracks to playlist.
    /// </summary>
    private void AddSelectedToPlaylistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedTracks = TracksDataGrid.SelectedItems
            .Cast<object>()
            .Select(item => GetTrackFromDataGridSelection(item))
            .Where(track => track != null)
            .Cast<TrackInfo>()
            .ToList();

        if (selectedTracks.Any())
        {
            AddTracksToPlaylist(selectedTracks);
        }
    }

    /// <summary>
    /// Helper to extract TrackInfo from either TrackInfo (single-night) or TrackViewItem (multi-night).
    /// Returns null if item is a DateHeaderItem or invalid type.
    /// </summary>
    private TrackInfo? GetTrackFromDataGridSelection(object? item)
    {
        if (item == null)
            return null;

        // Single-night view: item is directly TrackInfo
        if (item is TrackInfo track)
            return track;

        // Multi-night view: item is TrackViewItem wrapping TrackInfo
        if (item is TrackViewItem trackViewItem)
            return trackViewItem.Track;

        // DateHeaderItem or other type - not a track
        return null;
    }

    /// <summary>
    /// Context menu Opened event: Enables/disables menu items based on current selection.
    /// </summary>
    private void TracksContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);

        // Enable "Remove from Playlist" only if the track is currently in the playlist
        if (RemoveFromPlaylistMenuItem != null)
        {
            RemoveFromPlaylistMenuItem.IsEnabled = track != null && App.PlaybackService.Playlist.Contains(track);
        }

        // Enable "Add Selected to Playlist" only if multiple items are selected
        if (AddSelectedToPlaylistMenuItem != null)
        {
            AddSelectedToPlaylistMenuItem.IsEnabled = TracksDataGrid.SelectedItems.Count > 1;
        }
    }

    /// <summary>
    /// "Add All" button click handler: Adds all tracks for a specific concert date to the playlist.
    /// Button.Tag contains the date string (yyyy-MM-dd).
    /// </summary>
    private void AddAllTracksButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string date)
            return;

        // Get all tracks for this date from _currentTracks
        var tracksForDate = _currentTracks
            .Where(t => t.TrackDate == date)
            .ToList();

        if (tracksForDate.Any())
        {
            AddTracksToPlaylist(tracksForDate);

            // Show confirmation in status bar
            StatusText.Text = $"{tracksForDate.Count} tracks from {date} added to playlist";
            StatusText.Visibility = Visibility.Visible;

            // Clear status after 3 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                StatusText.Text = $"{_shows.Count} concerts in library";
            };
            timer.Start();
        }
    }


    // Menu handlers
    private void ImportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var importWindow = new MainWindow();
        importWindow.Closed += (s, e) => LoadShows(); // Refresh library when import closes
        importWindow.Show();
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(this, _librarySettings, _normalizationService);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();

        // Reload shows after settings change
        LoadShows();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Toggles PlayerWindow visibility (and its companion windows).
    /// Updates menu item appearance to reflect state.
    /// </summary>
    private void PlayerToggleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayerWindowVisibility();
    }

    /// <summary>
    /// Handles Ctrl+P keyboard shortcut to toggle PlayerWindow.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.P &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            TogglePlayerWindowVisibility();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Toggles PlayerWindow visibility with position reset.
    /// Three-state logic:
    /// - Hidden → show all at default position
    /// - Minimized → restore all to Normal
    /// - Normal/visible → hide all
    /// </summary>
    private void TogglePlayerWindowVisibility()
    {
        // Find PlayerWindow in app windows
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window is PlayerWindow playerWindow)
            {
                if (!playerWindow.IsVisible)
                {
                    // Hidden → show all at default position
                    ResetPlayerWindowPositions(playerWindow);
                    playerWindow.Show();
                    playerWindow.Activate();

                    // Update menu item appearance to show active state
                    PlayerToggleMenuItem.FontWeight = FontWeights.Bold;
                }
                else if (playerWindow.WindowState == WindowState.Minimized)
                {
                    // Minimized → restore all to Normal
                    playerWindow.WindowState = WindowState.Normal;
                    playerWindow.Activate();

                    if (playerWindow.PlaylistWindowInstance?.WindowState == WindowState.Minimized)
                        playerWindow.PlaylistWindowInstance.WindowState = WindowState.Normal;

                    if (playerWindow.VisWindowInstance?.WindowState == WindowState.Minimized)
                        playerWindow.VisWindowInstance.WindowState = WindowState.Normal;

                    // Update menu item appearance to show active state
                    PlayerToggleMenuItem.FontWeight = FontWeights.Bold;
                }
                else
                {
                    // Visible and normal → hide all
                    playerWindow.Hide();
                    playerWindow.PlaylistWindowInstance?.Hide();
                    playerWindow.VisWindowInstance?.Hide();

                    // Update menu item appearance to show inactive state
                    PlayerToggleMenuItem.FontWeight = FontWeights.Normal;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Resets all player windows to default size and position.
    /// Clears saved positions from settings to prevent off-screen placement.
    /// </summary>
    private void ResetPlayerWindowPositions(PlayerWindow playerWindow)
    {
        // Clear saved positions from settings
        _librarySettings.PlayerWindowLeft = null;
        _librarySettings.PlayerWindowTop = null;
        _librarySettings.PlaylistWindowLeft = null;
        _librarySettings.PlaylistWindowTop = null;
        _librarySettings.VisWindowLeft = null;
        _librarySettings.VisWindowTop = null;
        _librarySettings.Save();

        // Reset PlayerWindow to default size and position (center-bottom of primary screen)
        playerWindow.WindowState = WindowState.Normal;
        playerWindow.Width = 450;
        playerWindow.Height = 180;

        var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        playerWindow.Left = screen.Left + (screen.Width - playerWindow.Width) / 2;
        playerWindow.Top = screen.Top + screen.Height - playerWindow.Height - 40; // 40px from bottom

        // Reset PlaylistWindow to default size and attach below PlayerWindow
        if (playerWindow.PlaylistWindowInstance != null)
        {
            playerWindow.PlaylistWindowInstance.WindowState = WindowState.Normal;
            playerWindow.PlaylistWindowInstance.Width = 450;
            playerWindow.PlaylistWindowInstance.Height = 400;
            playerWindow.PlaylistWindowInstance.AttachToPlayerWindow();
        }

        // Reset VisWindow to default size and attach below PlaylistWindow
        if (playerWindow.VisWindowInstance != null)
        {
            playerWindow.VisWindowInstance.WindowState = WindowState.Normal;
            playerWindow.VisWindowInstance.Width = 450;
            playerWindow.VisWindowInstance.Height = 300;
            playerWindow.VisWindowInstance.AttachToPlaylistWindow();
        }
    }

    private void EditMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentShow == null)
        {
            MessageBox.Show("No concert is currently selected.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Open the Import Wizard with the current show's folder
        var importWindow = new MainWindow();
        importWindow.Owner = this;
        importWindow.LoadFolder(_currentShow.FolderPath);
        importWindow.Closed += (s, e) => LoadShows(); // Reload library when edit completes
        importWindow.Show();

        // Reload the concert view after editing to reflect any changes
        OpenConcertView(_currentShow);
    }

    private void DateLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string date)
        {
            ScrollToDate(date);
        }
    }

    private void JumpToDateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JumpToDateComboBox.SelectedItem is string date && !string.IsNullOrEmpty(date))
        {
            ScrollToDate(date);

            // Reset ComboBox to placeholder (no selection) after navigation
            JumpToDateComboBox.SelectedIndex = -1;
        }
    }

    private void TracksDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Check if the clicked item is a DateHeaderItem
        if (TracksDataGrid.SelectedItem is DateHeaderItem header)
        {
            ToggleDateSection(header);
        }
    }

    private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandAllSections();
    }

    private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseAllSections();
    }

    private void JerryBaseSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentShow == null) return;

        string searchTerm = "";

        if (_currentShow.Type == AlbumType.OfficialRelease)
        {
            // Search by album name
            searchTerm = !string.IsNullOrEmpty(_currentShow.OfficialRelease)
                ? _currentShow.OfficialRelease
                : _currentShow.AlbumName;
        }
        else
        {
            // Search by concert date
            searchTerm = _currentShow.Date;
        }

        if (string.IsNullOrEmpty(searchTerm)) return;

        // URL encode and construct Google search URL for jerrybase.com
        string encodedTerm = Uri.EscapeDataString(searchTerm);
        string url = $"https://www.google.com/search?q=site:jerrybase.com+{encodedTerm}";

        try
        {
            // Open URL in default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void UpdateLibraryRootDisplay(string path)
    {
        LoadShows();
    }

    public void ClearCurrentView()
    {
        _currentTracks.Clear();
        ShowsDataGrid.ItemsSource = null;
        _shows.Clear();
        LoadShows();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
    }

    // Search functionality
    private bool _isUpdatingSearchBox = false;
    private void QuickSearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingSearchBox) return; // Prevent recursive calls

        _quickSearchText = QuickSearchTextBox.Text?.Trim() ?? "";
        ClearSearchButton.Visibility = string.IsNullOrEmpty(_quickSearchText) ? Visibility.Collapsed : Visibility.Visible;
        ApplySearchFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingSearchBox = true;
        QuickSearchTextBox.Text = "";
        QuickSearchTextBox.IsReadOnly = false;
        _quickSearchText = "";
        _advancedSearchSongs.Clear();
        _advancedSearchExcludedSongs.Clear();
        _advancedSearchSequence.Clear();
        ClearSearchButton.Visibility = Visibility.Collapsed;
        _isUpdatingSearchBox = false;
        ApplySearchFilter();
    }

    private void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdvancedSearchDialog(_normalizationService, _metadataService, _librarySettings, this);
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            // Apply advanced search criteria
            _advancedSearchSongs = dialog.SelectedSongs;
            _advancedSearchExcludedSongs = dialog.ExcludedSongs;
            _advancedSearchSequence = dialog.SongSequence;

            ApplySearchFilter();

            // Update UI to show advanced search is active
            if (_advancedSearchSongs.Any() || _advancedSearchExcludedSongs.Any() || _advancedSearchSequence.Any())
            {
                var displayText = "";

                // Show song list
                if (_advancedSearchSongs.Any())
                {
                    if (_advancedSearchSongs.Count == 1)
                    {
                        displayText = _advancedSearchSongs[0];
                    }
                    else if (_advancedSearchSongs.Count <= 3)
                    {
                        displayText = string.Join(", ", _advancedSearchSongs);
                    }
                    else
                    {
                        displayText = $"{string.Join(", ", _advancedSearchSongs.Take(2))}, and {_advancedSearchSongs.Count - 2} more";
                    }
                }

                // Show excluded songs
                if (_advancedSearchExcludedSongs.Any())
                {
                    string excludeText;
                    if (_advancedSearchExcludedSongs.Count == 1)
                    {
                        excludeText = $"NOT {_advancedSearchExcludedSongs[0]}";
                    }
                    else if (_advancedSearchExcludedSongs.Count <= 3)
                    {
                        excludeText = $"NOT ({string.Join(", ", _advancedSearchExcludedSongs)})";
                    }
                    else
                    {
                        excludeText = $"NOT ({string.Join(", ", _advancedSearchExcludedSongs.Take(2))}, and {_advancedSearchExcludedSongs.Count - 2} more)";
                    }

                    if (!string.IsNullOrEmpty(displayText))
                    {
                        displayText += " " + excludeText;
                    }
                    else
                    {
                        displayText = excludeText;
                    }
                }

                // Show sequence
                if (_advancedSearchSequence.Any())
                {
                    var sequenceText = string.Join(" > ", _advancedSearchSequence);
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        displayText += " | Sequence: " + sequenceText;
                    }
                    else
                    {
                        displayText = "Sequence: " + sequenceText;
                    }
                }

                _isUpdatingSearchBox = true;
                QuickSearchTextBox.Text = displayText;
                QuickSearchTextBox.IsReadOnly = true;
                ClearSearchButton.Visibility = Visibility.Visible;
                _isUpdatingSearchBox = false;
            }
        }
    }

    private void TypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only apply filter if we're fully initialized
        if (_allShows == null) return;

        // Apply filter when selection changes
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        if (_allShows == null || _allShows.Count == 0)
        {
            _shows = new List<LibraryShow>();
            if (ShowsDataGrid != null)
            {
                ShowsDataGrid.ItemsSource = _shows;
            }
            if (StatusText != null)
            {
                StatusText.Text = "No concerts in library";
            }
            return;
        }

        // Check if we're in "By Date" mode
        var selectedFilterTag = "";
        if (TypeFilterComboBox != null && TypeFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            selectedFilterTag = selectedItem.Tag?.ToString() ?? "All";
        }

        if (selectedFilterTag == "ByDate")
        {
            // Switch to date-based view
            var concertDates = LoadConcertDates();
            ShowsDataGrid.ItemsSource = concertDates;
            SearchResultTextBlock.Text = $"{concertDates.Count} concert dates";
            StatusText.Text = $"{concertDates.Count} concert dates in library";
            return;
        }

        // Standard album-based view
        var filtered = _allShows.AsEnumerable();

        // Apply type filter from dropdown
        if (selectedFilterTag != "All")
        {
            filtered = filtered.Where(show =>
                (selectedFilterTag == "AudienceRecording" && show.Type == AlbumType.AudienceRecording) ||
                (selectedFilterTag == "OfficialRelease" && show.Type == AlbumType.OfficialRelease)
            );
        }

        // Apply quick search filter
        if (!string.IsNullOrEmpty(_quickSearchText))
        {
            var searchLower = _quickSearchText.ToLowerInvariant();
            filtered = filtered.Where(show =>
                show.Date.ToLowerInvariant().Contains(searchLower) ||
                show.Venue.ToLowerInvariant().Contains(searchLower) ||
                show.City.ToLowerInvariant().Contains(searchLower) ||
                show.State.ToLowerInvariant().Contains(searchLower) ||
                show.Location.ToLowerInvariant().Contains(searchLower) ||
                show.AlbumName.ToLowerInvariant().Contains(searchLower) ||
                show.Edition.ToLowerInvariant().Contains(searchLower) ||
                show.OfficialRelease.ToLowerInvariant().Contains(searchLower) ||
                (show.ReleaseYear.HasValue && show.ReleaseYear.Value.ToString().Contains(searchLower)) ||
                // Search within multi-date/multi-venue arrays for official releases
                show.ContainsDates.Any(d => d.ToLowerInvariant().Contains(searchLower)) ||
                show.ContainsVenues.Any(v => v.ToLowerInvariant().Contains(searchLower))
            );
        }

        // Apply advanced search filters (song-based searches require loading tracks)
        if (_advancedSearchSongs.Any() || _advancedSearchExcludedSongs.Any() || _advancedSearchSequence.Any())
        {
            // Force immediate evaluation to avoid lazy enumeration issues
            var filteredList = filtered.ToList();
            var matchingShows = new List<LibraryShow>();

            var matchCount = 0;
            var totalChecked = 0;

            foreach (var show in filteredList)
            {
                totalChecked++;
                if (ShowMatchesSongCriteria(show))
                {
                    matchCount++;
                    matchingShows.Add(show);
                }
            }

            _shows = matchingShows;

            // Log summary
            var logPath = Path.Combine(Path.GetTempPath(), "deadedit_search_debug.txt");
            try
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === SEARCH SUMMARY ===\n" +
                    $"Total concerts checked: {totalChecked}\n" +
                    $"Concerts with matching songs: {matchCount}\n" +
                    $"Final result count: {_shows.Count}\n" +
                    $"_allShows count: {_allShows.Count}\n\n");
            }
            catch { }
        }
        else
        {
            _shows = filtered.ToList();
        }

        ShowsDataGrid.ItemsSource = _shows;

        // Update status
        if (_shows.Count != _allShows.Count)
        {
            SearchResultTextBlock.Text = $"Showing {_shows.Count} of {_allShows.Count} concerts";

            if (_shows.Count == 0 && !string.IsNullOrEmpty(_quickSearchText) && !_advancedSearchSongs.Any() && !_advancedSearchExcludedSongs.Any() && !_advancedSearchSequence.Any())
            {
                StatusText.Text = $"No concerts match '{_quickSearchText}'. Quick search only searches date, venue, and location. Use Advanced Search to search by songs.";
            }
            else
            {
                StatusText.Text = $"{_shows.Count} concerts match search";
            }
        }
        else
        {
            SearchResultTextBlock.Text = "";
            StatusText.Text = $"{_shows.Count} concerts in library";
        }
    }

    private bool ShowMatchesSongCriteria(LibraryShow show)
    {
        try
        {
            // Load tracks for this show
            var tracks = _metadataService.ReadFolder(show.FolderPath);

            // Normalize the tracks
            _normalizationService.NormalizeAll(tracks);

            // Check if show contains required songs
            if (_advancedSearchSongs.Any())
            {
                // Get song names, filtering out empty ones
                var songNames = tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.SongName))
                    .Select(t => t.SongName!)
                    .ToList();

                // DEBUG: Log first show's details to help diagnose
                if (show == _allShows.FirstOrDefault())
                {
                    var logPath = Path.Combine(Path.GetTempPath(), "deadedit_search_debug.txt");
                    try
                    {
                        File.WriteAllText(logPath,
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === SONG SEARCH DEBUG ===\n" +
                            $"Show: {show.Date} - {show.Venue}\n" +
                            $"Required songs: {string.Join(", ", _advancedSearchSongs)}\n" +
                            $"Total tracks: {tracks.Count}\n" +
                            $"Song names found: {songNames.Count}\n" +
                            $"Titles: {string.Join(", ", songNames.Take(20))}\n" +
                            $"Log saved to: {logPath}\n");
                        System.Diagnostics.Debug.WriteLine($"Search debug log: {logPath}");
                    }
                    catch { }
                }

                foreach (var requiredSong in _advancedSearchSongs)
                {
                    // Check if any song name matches the required song (case-insensitive)
                    bool found = songNames.Any(title =>
                        title.Equals(requiredSong, StringComparison.OrdinalIgnoreCase));

                    // DEBUG: Log comparison details for first show
                    if (show == _allShows.FirstOrDefault())
                    {
                        var logPath = Path.Combine(Path.GetTempPath(), "deadedit_search_debug.txt");
                        try
                        {
                            File.AppendAllText(logPath,
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === COMPARISON DEBUG ===\n" +
                                $"Looking for: '{requiredSong}' (length: {requiredSong.Length})\n" +
                                $"Found: {found}\n" +
                                $"Checking against: {string.Join(" | ", songNames.Select(t => $"'{t}' (len:{t.Length})"))}\n\n");
                        }
                        catch { }
                    }

                    if (!found)
                    {
                        return false; // Missing required song
                    }
                }
            }

            // Check for excluded songs
            if (_advancedSearchExcludedSongs.Any())
            {
                // Get song names, filtering out empty ones
                var songNames = tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.SongName))
                    .Select(t => t.SongName!)
                    .ToList();

                // If the show contains ANY excluded song, reject it
                foreach (var excludedSong in _advancedSearchExcludedSongs)
                {
                    bool found = songNames.Any(title =>
                        title.Equals(excludedSong, StringComparison.OrdinalIgnoreCase));

                    if (found)
                    {
                        return false; // Show contains an excluded song
                    }
                }
            }

            // Check song sequence
            if (_advancedSearchSequence.Any())
            {
                if (!ShowContainsSequence(tracks, _advancedSearchSequence))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // Log error for debugging
            System.Diagnostics.Debug.WriteLine($"Error checking show {show.Date}: {ex.Message}");
            return false; // Error loading tracks
        }
    }

    private bool ShowContainsSequence(List<TrackInfo> tracks, List<string> sequence)
    {
        if (sequence.Count == 0) return true;

        var songNames = tracks.Select(t => t.SongName?.ToLowerInvariant() ?? "").ToList();

        // Look for consecutive songs in order
        for (int i = 0; i <= songNames.Count - sequence.Count; i++)
        {
            bool matchFound = true;
            for (int j = 0; j < sequence.Count; j++)
            {
                if (songNames[i + j] != sequence[j].ToLowerInvariant())
                {
                    matchFound = false;
                    break;
                }
            }
            if (matchFound)
            {
                return true;
            }
        }

        return false;
    }

    // Method to navigate to an album by its folder path (used by track search)
    public void NavigateToAlbumByPath(string albumPath)
    {
        // Find the matching album in our current shows list
        var matchingShow = _allShows.FirstOrDefault(show =>
            show.FolderPath.Equals(albumPath, StringComparison.OrdinalIgnoreCase));

        if (matchingShow != null)
        {
            // Open the album view
            OpenConcertView(matchingShow);
        }
        else
        {
            // Album not in current list, need to reload library to find it
            LoadShows();
            matchingShow = _allShows.FirstOrDefault(show =>
                show.FolderPath.Equals(albumPath, StringComparison.OrdinalIgnoreCase));

            if (matchingShow != null)
            {
                OpenConcertView(matchingShow);
            }
        }
    }
}

public class LibraryShow
{
    // Album type (defaults to Live)
    public AlbumType Type { get; set; } = AlbumType.AudienceRecording;

    // Live recording properties
    public string Date { get; set; } = "";
    public string Venue { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Location { get; set; } = "";
    public string OfficialRelease { get; set; } = "";

    // Multi-date/multi-venue support for official releases
    public List<string> ContainsDates { get; set; } = new List<string>();      // All dates in this release
    public List<string> ContainsVenues { get; set; } = new List<string>();     // All venues in this release

    // Studio album properties
    public string AlbumName { get; set; } = "";
    public int? ReleaseYear { get; set; }
    public string Edition { get; set; } = "";

    // Common properties
    public int TrackCount { get; set; }
    public List<string> FolderPaths { get; set; } = new List<string>();

    // Backward-compatible property for code not yet updated
    public string FolderPath
    {
        get => FolderPaths.FirstOrDefault() ?? "";
        set
        {
            if (FolderPaths.Count == 0)
                FolderPaths.Add(value);
            else
                FolderPaths[0] = value;
        }
    }

    // Smart display properties that adapt based on type
    public string TypeIcon => Type == AlbumType.OfficialRelease ? "📀" : "🎸";

    public string PrimaryInfo =>
        Type == AlbumType.OfficialRelease
            ? (!string.IsNullOrEmpty(OfficialRelease) ? OfficialRelease : AlbumName)
            : Date;

    public string SecondaryInfo =>
        Type == AlbumType.OfficialRelease
            ? (ContainsDates.Count > 1 ? string.Join(", ", ContainsDates) : (ReleaseYear.HasValue ? ReleaseYear.Value.ToString() : Date))
            : Venue;

    public string TertiaryInfo =>
        Type == AlbumType.OfficialRelease
            ? (ContainsVenues.Count > 1 ? string.Join(", ", ContainsVenues) : (!string.IsNullOrEmpty(Edition) ? Edition : Venue))
            : Location;

    // For backwards compatibility and display
    public string DisplayTitle =>
        Type == AlbumType.OfficialRelease
            ? (!string.IsNullOrEmpty(AlbumName)
                ? (ReleaseYear.HasValue ? $"{AlbumName} ({ReleaseYear.Value})" : AlbumName)
                : OfficialRelease)
            : Date;

    // Display album name only for Official Releases (blank for Audience Recordings)
    public string DisplayAlbumName =>
        Type == AlbumType.OfficialRelease
            ? (!string.IsNullOrEmpty(OfficialRelease) ? OfficialRelease : AlbumName)
            : "";

    public bool IsOfficialRelease => Type == AlbumType.OfficialRelease;
}

public class ConcertDate
{
    // Concert date (yyyy-MM-dd)
    public string Date { get; set; } = "";

    // Venue name
    public string Venue { get; set; } = "";

    // "City, State" location string
    public string Location { get; set; } = "";

    // Collection/Album name (e.g., "Enjoying the Ride" or "Audience Recording")
    public string CollectionName { get; set; } = "";

    // Track count for this specific date (if available without full file scan)
    public int TrackCount { get; set; }

    // Source LibraryShow this date came from (for opening the concert view)
    public LibraryShow SourceShow { get; set; } = null!;

    // Folder path(s) specific to this date (for multi-folder albums)
    public List<string> FolderPaths { get; set; } = new List<string>();

    // Icon for display (empty for date view)
    public string TypeIcon => "📅";

    // Display album name (reuse CollectionName for grid binding compatibility)
    public string DisplayAlbumName => CollectionName;
}

// Value converter for formatting date section headers
public class DateHeaderConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        // Debug logging
        System.Diagnostics.Debug.WriteLine($"DateHeaderConverter called with {values.Length} values");
        if (values.Length >= 1) System.Diagnostics.Debug.WriteLine($"  Value[0]: {values[0]} (type: {values[0]?.GetType().Name})");
        if (values.Length >= 2) System.Diagnostics.Debug.WriteLine($"  Value[1]: {values[1]} (type: {values[1]?.GetType().Name})");

        if (values.Length >= 2 && values[0] is string date && values[1] is Dictionary<string, (string venue, string location)> dateVenueMap)
        {
            System.Diagnostics.Debug.WriteLine($"  Attempting lookup for date: '{date}', map has {dateVenueMap.Count} entries");

            if (dateVenueMap.TryGetValue(date, out var venueInfo))
            {
                var result = $"{date} — {venueInfo.venue}, {venueInfo.location}";
                System.Diagnostics.Debug.WriteLine($"  SUCCESS: Returning '{result}'");
                return result;
            }

            System.Diagnostics.Debug.WriteLine($"  FALLBACK: Date '{date}' not found in map");
            // Fallback: just show the date
            return date;
        }

        System.Diagnostics.Debug.WriteLine($"  ERROR: Invalid binding types or count");
        return values[0]?.ToString() ?? "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
