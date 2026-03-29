using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfBinding = System.Windows.Data.Binding;

namespace DeadEditor
{
    public partial class LibraryGridView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibrarySettings _settings;
        private List<LibraryShow> _shows = new();
        private List<LibraryShow> _filteredShows = new();

        // By Date mode
        private bool _isByDateMode = false;
        private List<DateRow> _dateRows = new();
        private List<DateRow> _filteredDateRows = new();

        public int ConcertCount => _shows.Count;
        public int FilteredCount => _isByDateMode ? _filteredDateRows.Count : _filteredShows.Count;

        // Event to notify when concert count changes
        public event EventHandler<int>? ConcertCountChanged;

        public LibraryGridView(ShellWindow shell)
        {
            InitializeComponent();
            _shell = shell;
            _settings = LibrarySettings.Load();
        }

        private void LibraryGridView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadShows();
        }

        /// <summary>
        /// Public method called by ShellWindow after an import completes,
        /// so the grid refreshes to show the newly imported album.
        /// </summary>
        public void ReloadLibrary()
        {
            // Re-read settings in case the library path changed
            _settings.LibraryRootPath = LibrarySettings.Load().LibraryRootPath;
            _settings.OfficialReleasesPath = LibrarySettings.Load().OfficialReleasesPath;
            LoadShows();
        }

        private void LoadShows()
        {
            _shows.Clear();

            // Load audience recordings
            if (!string.IsNullOrEmpty(_settings.LibraryRootPath) && Directory.Exists(_settings.LibraryRootPath))
            {
                LoadAudienceRecordings();
            }

            // Load official releases
            if (!string.IsNullOrEmpty(_settings.OfficialReleasesPath) && Directory.Exists(_settings.OfficialReleasesPath))
            {
                LoadOfficialReleases();
            }

            // Sort by date descending (newest first)
            _shows = _shows.OrderByDescending(s => s.Date).ToList();

            if (_isByDateMode)
            {
                BuildDateRows();
                _filteredDateRows = new List<DateRow>(_dateRows);
                ShowsDataGrid.ItemsSource = _filteredDateRows;
                ConcertCountChanged?.Invoke(this, _dateRows.Count);
            }
            else
            {
                _filteredShows = new List<LibraryShow>(_shows);
                ShowsDataGrid.ItemsSource = _filteredShows;
                ConcertCountChanged?.Invoke(this, _shows.Count);
            }
        }

        // ===== COLUMN MANAGEMENT =====

        private void SetAlbumColumns()
        {
            ShowsDataGrid.Columns.Clear();
            ShowsDataGrid.Columns.Add(MakeColumn("Date", "Date", 100));
            ShowsDataGrid.Columns.Add(MakeColumn("Album Name", "AlbumName", 150));
            ShowsDataGrid.Columns.Add(MakeColumn("Venue", "Venue", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("City, State", "Location", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("Tracks", "TrackCount", 60));
        }

        private void SetDateColumns()
        {
            ShowsDataGrid.Columns.Clear();
            ShowsDataGrid.Columns.Add(MakeColumn("Date", "Date", 100));
            ShowsDataGrid.Columns.Add(MakeColumn("Venue", "Venue", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("City, State", "CityState", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("From Album", "FromAlbum", 150));
            ShowsDataGrid.Columns.Add(MakeColumn("Tracks", "TrackCount", 60));
        }

        private static DataGridTextColumn MakeColumn(string header, string bindingPath, double minWidth)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new WpfBinding(bindingPath),
                Width = DataGridLength.Auto,
                MinWidth = minWidth
            };
        }

        // ===== FILTERING =====

        /// <summary>
        /// Applies search text and type filter to the library grid.
        /// Called by ShellWindow when HeaderBar filter changes.
        /// </summary>
        public void ApplyFilter(string searchText, string typeFilter)
        {
            // Handle "By Date" mode
            if (typeFilter == "By Date")
            {
                if (!_isByDateMode)
                {
                    _isByDateMode = true;
                    BuildDateRows();
                    SetDateColumns();
                }

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var search = searchText.Trim();
                    _filteredDateRows = _dateRows.Where(r =>
                        ContainsIgnoreCase(r.Date, search)
                        || ContainsIgnoreCase(r.Venue, search)
                        || ContainsIgnoreCase(r.CityState, search)
                        || ContainsIgnoreCase(r.FromAlbum, search)
                    ).ToList();
                }
                else
                {
                    _filteredDateRows = new List<DateRow>(_dateRows);
                }

                ShowsDataGrid.ItemsSource = _filteredDateRows;
                ConcertCountChanged?.Invoke(this, _filteredDateRows.Count);
                return;
            }

            // Exiting By Date mode — restore album columns
            if (_isByDateMode)
            {
                _isByDateMode = false;
                SetAlbumColumns();
            }

            // Standard album filtering
            var results = _shows.AsEnumerable();

            if (typeFilter == "Official Releases")
            {
                results = results.Where(s => s.Type == AlbumType.OfficialRelease);
            }
            else if (typeFilter == "Audience Recordings")
            {
                results = results.Where(s => s.Type == AlbumType.AudienceRecording);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                var filtered = new List<LibraryShow>();
                foreach (var show in results)
                {
                    if (MatchesSearch(show, search))
                    {
                        filtered.Add(show);
                    }
                }
                _filteredShows = filtered;
            }
            else
            {
                _filteredShows = results.ToList();
            }

            ShowsDataGrid.ItemsSource = _filteredShows;
            ConcertCountChanged?.Invoke(this, _filteredShows.Count);
        }

        // ===== BY DATE MODE =====

        private void BuildDateRows()
        {
            _dateRows.Clear();
            foreach (var show in _shows)
            {
                // Try ContainsDates first, then extract from track titles, then single date
                var dates = show.ContainsDates.Count > 0
                    ? show.ContainsDates
                    : ExtractDatesFromTitles(show.TrackTitles);

                var albumLabel = !string.IsNullOrEmpty(show.AlbumName)
                    ? show.AlbumName
                    : (!string.IsNullOrEmpty(show.OfficialRelease) ? show.OfficialRelease : "");

                if (dates.Count > 1)
                {
                    // Multi-date album: one row per date
                    // Count tracks per date by matching title suffixes
                    var tracksByDate = CountTracksByDate(show.TrackTitles, dates);

                    foreach (var date in dates)
                    {
                        _dateRows.Add(new DateRow
                        {
                            Date = date,
                            Venue = show.Venue,
                            CityState = show.Location,
                            FromAlbum = albumLabel,
                            TrackCount = tracksByDate.GetValueOrDefault(date, 0),
                            SourceShow = show
                        });
                    }
                }
                else if (dates.Count == 1)
                {
                    _dateRows.Add(new DateRow
                    {
                        Date = dates[0],
                        Venue = show.Venue,
                        CityState = show.Location,
                        FromAlbum = albumLabel,
                        TrackCount = show.TrackCount,
                        SourceShow = show
                    });
                }
                else if (!string.IsNullOrEmpty(show.Date))
                {
                    // Fallback: use the show-level date
                    _dateRows.Add(new DateRow
                    {
                        Date = show.Date,
                        Venue = show.Venue,
                        CityState = show.Location,
                        FromAlbum = albumLabel,
                        TrackCount = show.TrackCount,
                        SourceShow = show
                    });
                }
            }
            _dateRows = _dateRows.OrderBy(r => r.Date).ToList();
        }

        /// <summary>
        /// Extracts unique yyyy-MM-dd dates from track title suffixes like "Song (1972-05-04)".
        /// </summary>
        private static List<string> ExtractDatesFromTitles(List<string> titles)
        {
            var dates = new HashSet<string>();
            var regex = new Regex(@"\((\d{4}-\d{2}-\d{2})");
            foreach (var title in titles)
            {
                var match = regex.Match(title);
                if (match.Success)
                    dates.Add(match.Groups[1].Value);
            }
            return dates.OrderBy(d => d).ToList();
        }

        /// <summary>
        /// Counts how many tracks belong to each date based on title suffixes.
        /// </summary>
        private static Dictionary<string, int> CountTracksByDate(List<string> titles, List<string> dates)
        {
            var counts = new Dictionary<string, int>();
            foreach (var date in dates)
                counts[date] = 0;

            var regex = new Regex(@"\((\d{4}-\d{2}-\d{2})");
            foreach (var title in titles)
            {
                var match = regex.Match(title);
                if (match.Success && counts.ContainsKey(match.Groups[1].Value))
                    counts[match.Groups[1].Value]++;
            }
            return counts;
        }

        // ===== SEARCH =====

        private static bool MatchesSearch(LibraryShow show, string search)
        {
            if (ContainsIgnoreCase(show.Date, search)
                || ContainsIgnoreCase(show.Venue, search)
                || ContainsIgnoreCase(show.City, search)
                || ContainsIgnoreCase(show.State, search)
                || ContainsIgnoreCase(show.Location, search)
                || ContainsIgnoreCase(show.AlbumName, search)
                || ContainsIgnoreCase(show.OfficialRelease, search)
                || ContainsIgnoreCase(show.Edition, search)
                || (show.ReleaseYear.HasValue && show.ReleaseYear.Value.ToString().Contains(search)))
            {
                return true;
            }

            foreach (var title in show.TrackTitles)
            {
                if (title.Contains(search, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string? source, string search)
        {
            return !string.IsNullOrEmpty(source)
                && source.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        // ===== TRACK TITLE LOADING =====

        /// <summary>
        /// Reads FLAC/MP3 TITLE tags from audio files in a folder and returns them as a list.
        /// Used to populate LibraryShow.TrackTitles for search matching.
        /// </summary>
        private static List<string> ReadTrackTitles(string folderPath)
        {
            var titles = new List<string>();
            try
            {
                var audioFiles = Directory.GetFiles(folderPath, "*.flac")
                    .Concat(Directory.GetFiles(folderPath, "*.mp3"));

                foreach (var file in audioFiles)
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(file);
                        var title = tagFile.Tag.Title;
                        if (!string.IsNullOrEmpty(title))
                        {
                            titles.Add(title);
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }
            catch
            {
                // Skip folders that can't be enumerated
            }
            return titles;
        }

        /// <summary>
        /// Reads custom Xiph Vorbis Comment fields (VENUE, CITYSTATE) from the first FLAC file
        /// in a folder and populates the LibraryShow. These fields are written by WriteMetadata
        /// during import/edit and allow Official Release metadata to round-trip through FLAC tags.
        /// </summary>
        private static void ReadCustomFieldsIntoShow(LibraryShow show, string folderPath)
        {
            try
            {
                var firstFlac = Directory.GetFiles(folderPath, "*.flac").FirstOrDefault();
                if (firstFlac == null) return;

                using var tagFile = TagLib.File.Create(firstFlac);
                if (tagFile is TagLib.Flac.File flacFile)
                {
                    var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                    if (xiph == null) return;

                    var venue = xiph.GetFirstField("VENUE");
                    var cityState = xiph.GetFirstField("CITYSTATE");

                    if (!string.IsNullOrEmpty(venue))
                        show.Venue = venue;

                    if (!string.IsNullOrEmpty(cityState))
                    {
                        show.Location = cityState;
                        var parts = cityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                        show.City = parts.Length > 0 ? parts[0] : "";
                        show.State = parts.Length > 1 ? parts[1] : "";
                    }

                    // Read year from FLAC tag if not already set from folder name
                    if (show.ReleaseYear == null && tagFile.Tag.Year > 0)
                    {
                        show.ReleaseYear = (int)tagFile.Tag.Year;
                    }
                }
            }
            catch
            {
                // Skip if file can't be read
            }
        }

        // ===== LIBRARY LOADING =====

        private void LoadAudienceRecordings()
        {
            var topFolders = Directory.GetDirectories(_settings.LibraryRootPath);

            foreach (var yearFolder in topFolders)
            {
                var yearName = Path.GetFileName(yearFolder);
                if (!Regex.IsMatch(yearName, @"^\d{4}$"))
                    continue;

                var showFolders = Directory.GetDirectories(yearFolder);

                foreach (var showFolder in showFolders)
                {
                    var folderName = Path.GetFileName(showFolder);
                    var parts = folderName.Split(new[] { " - " }, StringSplitOptions.None);

                    if (parts.Length >= 2)
                    {
                        string date = parts[0];
                        string venue = "";
                        string city = "";
                        string state = "";

                        if (parts.Length == 3)
                        {
                            venue = parts[1];
                            var locationParts = parts[2].Split(new[] { ", " }, StringSplitOptions.None);
                            city = locationParts.Length > 0 ? locationParts[0] : "";
                            state = locationParts.Length > 1 ? locationParts[1] : "";
                        }
                        else if (parts.Length == 2)
                        {
                            var venueParts = parts[1].Split(new[] { ", " }, StringSplitOptions.None);
                            venue = venueParts.Length > 0 ? venueParts[0] : "";
                            city = venueParts.Length > 1 ? venueParts[1] : "";
                            state = venueParts.Length > 2 ? venueParts[2] : "";
                        }

                        var audioFiles = Directory.GetFiles(showFolder, "*.flac").Concat(Directory.GetFiles(showFolder, "*.mp3")).ToArray();

                        _shows.Add(new LibraryShow
                        {
                            Type = AlbumType.AudienceRecording,
                            Date = date,
                            Venue = venue,
                            City = city,
                            State = state,
                            Location = !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state) ? $"{city}, {state}" : city + state,
                            TrackCount = audioFiles.Length,
                            FolderPath = showFolder,
                            TrackTitles = ReadTrackTitles(showFolder)
                        });
                    }
                }
            }
        }

        private void LoadOfficialReleases()
        {
            // Load from Studio Albums folder
            var studioPath = Path.Combine(_settings.OfficialReleasesPath, "Studio Albums");
            if (Directory.Exists(studioPath))
            {
                foreach (var albumFolder in Directory.GetDirectories(studioPath))
                {
                    var folderName = Path.GetFileName(albumFolder);
                    var audioFiles = Directory.GetFiles(albumFolder, "*.flac").Concat(Directory.GetFiles(albumFolder, "*.mp3")).ToArray();

                    string albumName = folderName;
                    int? year = null;

                    var yearMatch = Regex.Match(folderName, @"^(.+?)\s*\((\d{4})\)");
                    if (yearMatch.Success)
                    {
                        albumName = yearMatch.Groups[1].Value.Trim();
                        if (int.TryParse(yearMatch.Groups[2].Value, out var y))
                            year = y;
                    }

                    var studioTitles = ReadTrackTitles(albumFolder);
                    var studioDates = ExtractDatesFromTitles(studioTitles);

                    var studioShow = new LibraryShow
                    {
                        Type = AlbumType.OfficialRelease,
                        AlbumName = albumName,
                        ReleaseYear = year,
                        Date = studioDates.Count > 0 ? studioDates[0] : "",
                        ContainsDates = studioDates,
                        TrackCount = audioFiles.Length,
                        FolderPath = albumFolder,
                        TrackTitles = studioTitles
                    };
                    ReadCustomFieldsIntoShow(studioShow, albumFolder);
                    _shows.Add(studioShow);
                }
            }

            // Load from series folders (Dave's Picks, etc.)
            var seriesFolders = Directory.GetDirectories(_settings.OfficialReleasesPath).Where(f => !Path.GetFileName(f).Equals("Studio Albums", StringComparison.OrdinalIgnoreCase));

            foreach (var seriesFolder in seriesFolders)
            {
                foreach (var releaseFolder in Directory.GetDirectories(seriesFolder))
                {
                    var folderName = Path.GetFileName(releaseFolder);
                    var audioFiles = Directory.GetFiles(releaseFolder, "*.flac").Concat(Directory.GetFiles(releaseFolder, "*.mp3")).ToArray();

                    if (audioFiles.Length == 0) continue;

                    var seriesTitles = ReadTrackTitles(releaseFolder);
                    var seriesDates = ExtractDatesFromTitles(seriesTitles);

                    var seriesShow = new LibraryShow
                    {
                        Type = AlbumType.OfficialRelease,
                        AlbumName = folderName,
                        OfficialRelease = folderName,
                        Date = seriesDates.Count > 0 ? seriesDates[0] : "",
                        ContainsDates = seriesDates,
                        TrackCount = audioFiles.Length,
                        FolderPath = releaseFolder,
                        TrackTitles = seriesTitles
                    };
                    ReadCustomFieldsIntoShow(seriesShow, releaseFolder);
                    _shows.Add(seriesShow);
                }
            }
        }

        // ===== NAVIGATION =====

        private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            LibraryShow? show = null;

            if (ShowsDataGrid.SelectedItem is LibraryShow directShow)
            {
                show = directShow;
            }
            else if (ShowsDataGrid.SelectedItem is DateRow dateRow)
            {
                show = dateRow.SourceShow;
            }

            if (show != null)
            {
                var albumView = new AlbumDetailView(_shell, show);
                _shell.Navigation.NavigateTo(albumView, show);
            }
        }
    }
}
