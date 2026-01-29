using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor;

public partial class LibraryBrowserWindow : Window
{
    private readonly LibrarySettings _librarySettings;
    private readonly MetadataService _metadataService;
    private readonly NormalizationService _normalizationService;
    private readonly AudioPlayerService _audioPlayer;
    private List<LibraryShow> _shows = new();
    private List<LibraryShow> _allShows = new();  // Unfiltered list for search
    private List<TrackInfo> _currentTracks = new();
    private LibraryShow? _currentShow = null;
    private int _currentTrackIndex = -1;
    private DispatcherTimer? _updateTimer;
    private bool _isScrubbing = false;
    private bool _isManualTrackChange = false;
    private string _quickSearchText = "";
    private List<string> _advancedSearchSongs = new();
    private List<string> _advancedSearchExcludedSongs = new();
    private List<string> _advancedSearchSequence = new();

    public LibraryBrowserWindow()
    {
        InitializeComponent();

        _librarySettings = LibrarySettings.Load();
        _metadataService = new MetadataService();
        _normalizationService = new NormalizationService();
        _audioPlayer = new AudioPlayerService();

        // Set up audio player events
        _audioPlayer.PlaybackStopped += AudioPlayer_PlaybackStopped;

        // Set up update timer for scrubber
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Restore window position
        RestoreWindowPosition();

        // Save window position when closing
        Closing += LibraryBrowserWindow_Closing;

        // Load shows
        LoadShows();
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

            switch (cmd)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE:
                    PlayPauseButton_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;

                case APPCOMMAND_MEDIA_STOP:
                    StopButton_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;

                case APPCOMMAND_MEDIA_NEXTTRACK:
                    NextButton_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;

                case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                    PreviousButton_Click(this, new RoutedEventArgs());
                    handled = true;
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

        // Sort by type first (Live, Official Release, Studio), then by date/name
        _allShows = _allShows
            .OrderBy(s => s.Type)
            .ThenByDescending(s => s.Type == AlbumType.Live ? s.Date : s.AlbumName)
            .ToList();

        // Apply any active search filters
        ApplySearchFilter();
    }

    private void LoadAudienceRecordings()
    {

        // Scan year folders and special folders (like "Studio Albums")
        var topLevelFolders = Directory.GetDirectories(_librarySettings.LibraryRootPath);

        foreach (var topFolder in topLevelFolders)
        {
            var topFolderName = Path.GetFileName(topFolder);

            // Check if this is the "Studio Albums" folder
            if (topFolderName.Equals("Studio Albums", StringComparison.OrdinalIgnoreCase))
            {
                // Load studio albums
                var albumFolders = Directory.GetDirectories(topFolder);

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
                        Type = AlbumType.Studio,
                        AlbumName = albumName,
                        ReleaseYear = releaseYear,
                        Edition = edition,
                        TrackCount = audioFiles.Length,
                        FolderPath = albumFolder
                    });
                }
            }
            else
            {
                // Load live recordings from year folders
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
                        AlbumType albumType = AlbumType.Live;

                        if (audioFiles.Length > 0)
                        {
                            try
                            {
                                using (var tagFile = TagLib.File.Create(audioFiles[0]))
                                {
                                    var album = tagFile.Tag.Album ?? "";

                                    // Check for Box Set format first: "Date - Venue - City, State: Box Set Name" (no space before colon)
                                    var boxSetMatch = System.Text.RegularExpressions.Regex.Match(
                                        album, @":\s*([^:]+)$");

                                    // Check if there's a space before the colon (Official Release) or not (Box Set)
                                    var spaceBeforeColonMatch = System.Text.RegularExpressions.Regex.Match(
                                        album, @"\s:\s*(.+)$");

                                    if (spaceBeforeColonMatch.Success)
                                    {
                                        // Official Release format: "... : Release Name" (space before colon)
                                        officialRelease = spaceBeforeColonMatch.Groups[1].Value.Trim();
                                        albumType = AlbumType.Live;
                                    }
                                    else if (boxSetMatch.Success)
                                    {
                                        // Box Set format: "...: Box Set Name" (no space before colon)
                                        boxSetName = boxSetMatch.Groups[1].Value.Trim();
                                        albumType = AlbumType.BoxSet;
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
                            OfficialRelease = albumType == AlbumType.Live ? officialRelease : boxSetName,
                            TrackCount = audioFiles.Length,
                            FolderPath = showFolder
                        });
                    }
                }
            }
        }

    }

    private void LoadOfficialReleases()
    {
        // Scan for series folders (Dave's Picks, Road Trips, Dick's Picks, etc.)
        var seriesFolders = Directory.GetDirectories(_librarySettings.OfficialReleasesPath);

        foreach (var seriesFolder in seriesFolders)
        {
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

                foreach (var audioFile in audioFiles)
                {
                    try
                    {
                        using (var tagFile = TagLib.File.Create(audioFile))
                        {
                            var title = tagFile.Tag.Title ?? "";
                            var album = tagFile.Tag.Album ?? "";

                            // Extract official release from album tag if not already set
                            if (string.IsNullOrEmpty(officialRelease))
                            {
                                // Look for patterns like "Dave's Picks Volume 38", "Road Trips, Vol. 3 No. 4"
                                var releaseMatch = System.Text.RegularExpressions.Regex.Match(
                                    album,
                                    @"((?:Dave's Picks|Dick's Picks|Road Trips|Download Series)\s*,?\s*Vol(?:ume|\.)?\s+\d+(?:\s+No\.\s+\d+)?)",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (releaseMatch.Success)
                                {
                                    officialRelease = releaseMatch.Groups[1].Value.Trim();
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

                // Create LibraryShow entry
                _allShows.Add(new LibraryShow
                {
                    Type = AlbumType.OfficialRelease,
                    OfficialRelease = officialRelease,
                    Date = dates.Count > 0 ? dates.OrderBy(d => d).First() : "",  // First date
                    ContainsDates = dates.OrderBy(d => d).ToList(),
                    ContainsVenues = venues.OrderBy(v => v).ToList(),
                    TrackCount = audioFiles.Length,
                    FolderPath = releaseFolder,
                    // For display purposes, use first date/venue if available
                    Venue = venues.FirstOrDefault() ?? "",
                    City = "",
                    State = "",
                    Location = venues.Count > 0 ? string.Join(", ", venues) : ""
                });
            }
        }
    }

    private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShowsDataGrid.SelectedItem is LibraryShow show)
        {
            OpenConcertView(show);
        }
    }

    private void OpenConcertView(LibraryShow show)
    {
        try
        {
            // Save reference to current show
            _currentShow = show;

            // Load info based on album type
            if (show.Type == AlbumType.Studio)
            {
                // Studio album - show album name and year
                VenueText.Text = show.AlbumName;
                LocationText.Text = show.ReleaseYear.HasValue ? $"Released {show.ReleaseYear.Value}" : "Studio Album";
                DateText.Text = "";
                BoxSetText.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Live recording - show venue, location, and date
                VenueText.Text = show.Venue;
                LocationText.Text = show.Location;
                DateText.Text = show.Date;

                // Show box set name if this is a box set
                if (show.Type == AlbumType.BoxSet && !string.IsNullOrEmpty(show.OfficialRelease))
                {
                    BoxSetText.Text = $"Box Set: {show.OfficialRelease}";
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

            // Load tracks
            _currentTracks = _metadataService.ReadFolder(show.FolderPath);

            // Update preview metadata for each track
            foreach (var track in _currentTracks)
            {
                // Studio albums don't append dates to track titles
                if (show.Type == AlbumType.Studio)
                {
                    track.PreviewMetadata = track.GetFinalMetadataTitle(null);
                }
                else
                {
                    track.PreviewMetadata = track.GetFinalMetadataTitle(show.Date);
                }
            }

            TracksDataGrid.ItemsSource = _currentTracks;
            TrackCountText.Text = $"{_currentTracks.Count} tracks";

            // Enable play button if we have tracks
            if (_currentTracks.Count > 0)
            {
                PlayPauseButton.IsEnabled = true;
            }

            // Switch views
            LibraryView.Visibility = Visibility.Collapsed;
            ConcertView.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            // Show player controls only if not already visible
            if (PlayerControls.Visibility != Visibility.Visible)
            {
                PlayerControls.Visibility = Visibility.Visible;
            }

            // Update window title
            Title = $"{show.Date} - {show.Venue}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading concert: {ex.Message}", "Error",
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
            var index = _currentTracks.IndexOf(track);
            if (index >= 0)
            {
                PlayTrack(index);
            }
        }
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _currentTracks.Count) return;

        var track = _currentTracks[index];

        // Set flag to prevent auto-advance when changing tracks manually
        _isManualTrackChange = true;
        _currentTrackIndex = index;

        try
        {
            _audioPlayer.LoadFile(track.FilePath);
            _audioPlayer.Play();

            // Update UI
            PlayPauseButton.Content = "⏸";
            PlayPauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            PreviousButton.IsEnabled = _currentTrackIndex > 0;
            NextButton.IsEnabled = _currentTrackIndex < _currentTracks.Count - 1;

            NowPlayingText.Text = $"♪ {track.Title}";
            NowPlayingDetails.Text = $"Track {track.TrackNumber} - {track.Duration}";

            // Set up scrubber
            AudioScrubber.Maximum = _audioPlayer.TotalDuration.TotalSeconds;
            TotalTimeText.Text = FormatTime(_audioPlayer.TotalDuration);

            // Start update timer
            _updateTimer?.Start();

            // Clear the flag after a short delay
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isManualTrackChange = false;
            }), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error playing track: {ex.Message}", "Playback Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _isManualTrackChange = false;
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        // Use button content as the source of truth for current state
        // This prevents the double-click issue caused by state checking
        if (PlayPauseButton.Content.ToString() == "⏸")
        {
            // Currently playing - pause it
            _audioPlayer.Pause();
            PlayPauseButton.Content = "▶";
            _updateTimer?.Stop();
        }
        else
        {
            // Currently paused or stopped - resume or start playing
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _currentTracks.Count)
            {
                _audioPlayer.Play();
                PlayPauseButton.Content = "⏸";
                _updateTimer?.Start();
            }
            else if (_currentTracks.Count > 0)
            {
                // Start playing from first track
                PlayTrack(0);
            }
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _audioPlayer.Stop();
        _updateTimer?.Stop();
        PlayPauseButton.Content = "▶";
        AudioScrubber.Value = 0;
        CurrentTimeText.Text = "0:00";
        StopButton.IsEnabled = false;
        PlayPauseButton.IsEnabled = _currentTracks.Count > 0;
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrackIndex > 0)
        {
            PlayTrack(_currentTrackIndex - 1);
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrackIndex < _currentTracks.Count - 1)
        {
            PlayTrack(_currentTrackIndex + 1);
        }
    }

    private void AudioPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _updateTimer?.Stop();
            PlayPauseButton.Content = "▶";

            // Auto-advance to next track if not manually changing tracks
            if (!_isManualTrackChange && _currentTrackIndex >= 0 && _currentTrackIndex < _currentTracks.Count - 1)
            {
                PlayTrack(_currentTrackIndex + 1);
            }
            else if (_currentTrackIndex >= _currentTracks.Count - 1)
            {
                // End of playlist
                AudioScrubber.Value = 0;
                CurrentTimeText.Text = "0:00";
                NowPlayingText.Text = "Playlist ended";
                NowPlayingDetails.Text = "";
                StopButton.IsEnabled = false;
                PlayPauseButton.IsEnabled = _currentTracks.Count > 0;
                PreviousButton.IsEnabled = false;
                NextButton.IsEnabled = false;
            }
        });
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isScrubbing)
        {
            var currentPos = _audioPlayer.CurrentPosition;
            AudioScrubber.Value = currentPos.TotalSeconds;
            CurrentTimeText.Text = FormatTime(currentPos);
        }
    }

    private void AudioScrubber_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isScrubbing = true;
    }

    private void AudioScrubber_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isScrubbing = false;
        _audioPlayer.Seek(TimeSpan.FromSeconds(AudioScrubber.Value));
    }

    private void AudioScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isScrubbing)
        {
            CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioPlayer != null)
        {
            _audioPlayer.Volume = (float)(e.NewValue / 100.0);
        }
    }

    private string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        return $"{time.Minutes}:{time.Seconds:D2}";
    }

    // Menu handlers
    private void ImportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var importWindow = new MainWindow();
        importWindow.ShowDialog();

        // Refresh library after import
        LoadShows();
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
        importWindow.ShowDialog();

        // Reload the library list to reflect any metadata changes (e.g., box set names)
        LoadShows();

        // Reload the concert view after editing to reflect any changes
        OpenConcertView(_currentShow);
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
        _audioPlayer?.Dispose();
        _updateTimer?.Stop();
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

        // Start with all shows
        var filtered = _allShows.AsEnumerable();

        // Apply type filter from dropdown
        if (TypeFilterComboBox != null && TypeFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var filterTag = selectedItem.Tag?.ToString() ?? "All";
            if (filterTag != "All")
            {
                filtered = filtered.Where(show =>
                    (filterTag == "Live" && show.Type == AlbumType.Live) ||
                    (filterTag == "OfficialRelease" && show.Type == AlbumType.OfficialRelease) ||
                    (filterTag == "Studio" && show.Type == AlbumType.Studio)
                );
            }
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
                // Get normalized titles, filtering out empty ones
                var normalizedTitles = tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.NormalizedTitle))
                    .Select(t => t.NormalizedTitle!)
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
                            $"Normalized titles found: {normalizedTitles.Count}\n" +
                            $"Titles: {string.Join(", ", normalizedTitles.Take(20))}\n" +
                            $"Log saved to: {logPath}\n");
                        System.Diagnostics.Debug.WriteLine($"Search debug log: {logPath}");
                    }
                    catch { }
                }

                foreach (var requiredSong in _advancedSearchSongs)
                {
                    // Check if any normalized title matches the required song (case-insensitive)
                    bool found = normalizedTitles.Any(title =>
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
                                $"Checking against: {string.Join(" | ", normalizedTitles.Select(t => $"'{t}' (len:{t.Length})"))}\n\n");
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
                // Get normalized titles, filtering out empty ones
                var normalizedTitles = tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.NormalizedTitle))
                    .Select(t => t.NormalizedTitle!)
                    .ToList();

                // If the show contains ANY excluded song, reject it
                foreach (var excludedSong in _advancedSearchExcludedSongs)
                {
                    bool found = normalizedTitles.Any(title =>
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

        var normalizedTitles = tracks.Select(t => t.NormalizedTitle?.ToLowerInvariant() ?? "").ToList();

        // Look for consecutive songs in order
        for (int i = 0; i <= normalizedTitles.Count - sequence.Count; i++)
        {
            bool matchFound = true;
            for (int j = 0; j < sequence.Count; j++)
            {
                if (normalizedTitles[i + j] != sequence[j].ToLowerInvariant())
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
    public AlbumType Type { get; set; } = AlbumType.Live;

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
    public string FolderPath { get; set; } = "";

    // Smart display properties that adapt based on type
    public string TypeIcon => Type == AlbumType.Studio ? "💿" :
                              Type == AlbumType.OfficialRelease ? "📀" : "🎸";

    public string PrimaryInfo =>
        Type == AlbumType.Studio ? AlbumName :
        Type == AlbumType.OfficialRelease ? OfficialRelease :
        Date;

    public string SecondaryInfo =>
        Type == AlbumType.Studio ? (ReleaseYear.HasValue ? ReleaseYear.Value.ToString() : "") :
        Type == AlbumType.OfficialRelease ? (ContainsDates.Count > 1 ? string.Join(", ", ContainsDates) : Date) :
        Venue;

    public string TertiaryInfo =>
        Type == AlbumType.Studio ? Edition :
        Type == AlbumType.OfficialRelease ? (ContainsVenues.Count > 1 ? string.Join(", ", ContainsVenues) : Venue) :
        Location;

    // For backwards compatibility and display
    public string DisplayTitle =>
        Type == AlbumType.Studio
            ? (ReleaseYear.HasValue ? $"{AlbumName} ({ReleaseYear.Value})" : AlbumName)
            : Date;

    public bool IsStudioAlbum => Type == AlbumType.Studio;
}
