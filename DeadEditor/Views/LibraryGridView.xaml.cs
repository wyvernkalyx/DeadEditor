using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        private bool _isDateEditMode = false;
        private List<DateRow> _dateRows = new();
        private List<DateRow> _filteredDateRows = new();
        private Dictionary<string, (string Venue, string CityState)>? _editSnapshot;

        // Shows I Don't Have mode
        private bool _isMissingShowsMode = false;
        private List<DateRow> _missingShowRows = new();
        private List<DateRow> _filteredMissingRows = new();

        // Guard: LoadShowsAsync runs only on first Loaded event, not on back-navigation re-adds
        private bool _isInitialLoadComplete;

        // Remember last-applied filter so LoadShowsAsync can re-apply after reload
        private string _lastSearchText = "";
        private string _lastTypeFilter = "All";
        private string _lastYearFilter = "All Years";

        public int ConcertCount => _shows.Count;
        public int MissingShowCount => _missingShowRows.Count;
        public int TotalShowsInScope { get; private set; }
        public int FilteredCount => _isMissingShowsMode ? _filteredMissingRows.Count
            : _isByDateMode ? _filteredDateRows.Count : _filteredShows.Count;
        public bool IsByDateMode => _isByDateMode;
        public bool IsMissingShowsMode => _isMissingShowsMode;
        public bool IsDateEditMode => _isDateEditMode;

        // Event to notify when concert count changes
        public event EventHandler<int>? ConcertCountChanged;

        public LibraryGridView(ShellWindow shell)
        {
            InitializeComponent();
            _shell = shell;
            _settings = LibrarySettings.Load();
        }

        private async void LibraryGridView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialLoadComplete) return;
            _isInitialLoadComplete = true;
            await LoadShowsAsync();
        }

        /// <summary>
        /// Public method called by ShellWindow after an import completes,
        /// so the grid refreshes to show the newly imported album.
        /// </summary>
        public async void ReloadLibrary()
        {
            // Re-read settings in case the library path changed
            _settings.LibraryRootPath = LibrarySettings.Load().LibraryRootPath;
            await LoadShowsAsync();
        }

        private async Task LoadShowsAsync()
        {
            // Show loading indicator while scanning
            LoadingIndicator.Visibility = Visibility.Visible;

            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[STARTUP] LoadShowsAsync begin: {sw.ElapsedMilliseconds}ms");

            // Capture settings for background thread
            var libraryRoot = _settings.LibraryRootPath;

            // Run all heavy I/O (folder scanning, TagLib reads) on a background thread
            var shows = await Task.Run(() =>
            {
                var result = new List<LibraryShow>();

                if (!string.IsNullOrEmpty(libraryRoot) && Directory.Exists(libraryRoot))
                {
                    LoadAlbumsInto(result, libraryRoot);
                    Debug.WriteLine($"[STARTUP] Albums scanned: {sw.ElapsedMilliseconds}ms ({result.Count} shows)");

                    // Merge albums that share the same ALBUMNAME into single entries
                    // (e.g., box sets split across multiple folders)
                    MergeOfficialReleasesByAlbumName(result, 0);
                    Debug.WriteLine($"[STARTUP] After merge: {sw.ElapsedMilliseconds}ms ({result.Count} shows)");
                }

                // Sort by date descending (newest first)
                result = result.OrderByDescending(s => s.Date).ToList();
                return result;
            });

            Debug.WriteLine($"[STARTUP] Background scan complete: {sw.ElapsedMilliseconds}ms — {shows.Count} total shows");

            // Back on UI thread — populate the grid
            _shows = shows;

            if (_isByDateMode)
            {
                SetDateColumns();
                BuildDateRows();
                _filteredDateRows = new List<DateRow>(_dateRows);
                ShowsDataGrid.ItemsSource = _filteredDateRows;
                ConcertCountChanged?.Invoke(this, _dateRows.Count);
            }
            else if (_isMissingShowsMode)
            {
                SetMissingShowColumns();
                BuildMissingShowRows();
                _filteredMissingRows = new List<DateRow>(_missingShowRows);
                ShowsDataGrid.ItemsSource = _filteredMissingRows;
                ConcertCountChanged?.Invoke(this, _missingShowRows.Count);
            }
            else
            {
                SetAlbumColumns();
                _filteredShows = new List<LibraryShow>(_shows);
                ShowsDataGrid.ItemsSource = _filteredShows;
                ConcertCountChanged?.Invoke(this, _shows.Count);
            }

            // Hide loading indicator
            LoadingIndicator.Visibility = Visibility.Collapsed;

            // Re-apply last filter if one was active (covers ReloadLibrary after import/settings)
            if (_lastTypeFilter != "All" || !string.IsNullOrEmpty(_lastSearchText))
            {
                ApplyFilter(_lastSearchText, _lastTypeFilter, _lastYearFilter);
            }

            Debug.WriteLine($"[STARTUP] Grid populated, window visible: {sw.ElapsedMilliseconds}ms");
        }

        // ===== COLUMN MANAGEMENT =====

        private void SetAlbumColumns()
        {
            ShowsDataGrid.Columns.Clear();
            ShowsDataGrid.Columns.Add(MakeHeadyColumn());
            ShowsDataGrid.Columns.Add(MakeColumn("Date", "Date", 100));
            ShowsDataGrid.Columns.Add(MakeColumn("Album Name", "AlbumName", 150));
            ShowsDataGrid.Columns.Add(MakeColumn("Venue", "Venue", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("City, State", "Location", 120));
            ShowsDataGrid.Columns.Add(MakeColumn("Tracks", "TrackCount", 60));
            ShowsDataGrid.IsReadOnly = true;
        }

        private void SetDateColumns()
        {
            ShowsDataGrid.Columns.Clear();
            ShowsDataGrid.Columns.Add(MakeHeadyColumn());
            ShowsDataGrid.Columns.Add(MakeColumn("Date", "Date", 100));
            ShowsDataGrid.Columns.Add(MakeColumn("Venue", "Venue", 120, editable: true));
            ShowsDataGrid.Columns.Add(MakeColumn("City, State", "CityState", 120, editable: true));
            ShowsDataGrid.Columns.Add(MakeColumn("From Album", "FromAlbum", 150));
            ShowsDataGrid.Columns.Add(MakeColumn("Tracks", "TrackCount", 60));
            // Grid starts read-only — editing enabled explicitly via EnterEditMode()
            ShowsDataGrid.IsReadOnly = true;
        }

        private void SetMissingShowColumns()
        {
            ShowsDataGrid.Columns.Clear();
            ShowsDataGrid.Columns.Add(MakeHeadyColumn());
            ShowsDataGrid.Columns.Add(MakeColumn("Date", "Date", 100));
            ShowsDataGrid.Columns.Add(MakeColumn("Venue", "Venue", 200));
            ShowsDataGrid.Columns.Add(MakeColumn("City, State", "CityState", 150));
            ShowsDataGrid.IsReadOnly = true;
        }

        /// <summary>
        /// Creates a narrow column that displays a gold ⚡ icon for rows with heady versions.
        /// </summary>
        private static DataGridTemplateColumn MakeHeadyColumn()
        {
            // Build the DataTemplate with a TextBlock bound to HeadyIcon
            var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new WpfBinding("HeadyIcon"));
            factory.SetBinding(System.Windows.Controls.TextBlock.ToolTipProperty, new WpfBinding("HeadyTooltip"));
            factory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 16.0);
            factory.SetValue(System.Windows.Controls.TextBlock.ForegroundProperty,
                new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D4A017")));
            factory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            factory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            factory.SetValue(System.Windows.Controls.TextBlock.CursorProperty, System.Windows.Input.Cursors.Hand);

            var template = new DataTemplate { VisualTree = factory };

            return new DataGridTemplateColumn
            {
                Header = "\u26A1",
                CellTemplate = template,
                Width = new DataGridLength(30),
                MinWidth = 30,
                MaxWidth = 36,
                IsReadOnly = true
            };
        }

        private static DataGridTextColumn MakeColumn(string header, string bindingPath, double minWidth, bool editable = false)
        {
            var binding = new WpfBinding(bindingPath);
            if (editable)
                binding.Mode = System.Windows.Data.BindingMode.TwoWay;

            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = DataGridLength.Auto,
                MinWidth = minWidth,
                IsReadOnly = !editable,
                // Dark-themed editing style for editable cells
                EditingElementStyle = editable ? CreateEditingStyle() : null
            };
        }

        private static Style CreateEditingStyle()
        {
            var style = new Style(typeof(System.Windows.Controls.TextBox));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E"))));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC"))));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            style.Setters.Add(new Setter(System.Windows.Controls.TextBox.CaretBrushProperty, new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))));
            return style;
        }

        // ===== EDIT MODE =====

        /// <summary>
        /// Enters edit mode for By Date view — snapshots current values and enables editing.
        /// </summary>
        public void EnterDateEditMode()
        {
            if (!_isByDateMode || _isDateEditMode) return;

            // Snapshot current values for Cancel
            _editSnapshot = new Dictionary<string, (string, string)>();
            foreach (var row in _dateRows)
            {
                _editSnapshot[row.Date] = (row.Venue, row.CityState);
            }

            _isDateEditMode = true;
            ShowsDataGrid.IsReadOnly = false;
        }

        /// <summary>
        /// Saves all pending edits to shows.json and exits edit mode.
        /// </summary>
        public void SaveDateEdits()
        {
            if (!_isDateEditMode) return;

            // Commit any active cell edit
            ShowsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ShowsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Persist all rows that changed
            if (_editSnapshot != null)
            {
                bool anyChanged = false;
                foreach (var row in _dateRows)
                {
                    if (_editSnapshot.TryGetValue(row.Date, out var old)
                        && (old.Venue != row.Venue || old.CityState != row.CityState))
                    {
                        PersistDateRowToShows(row, save: false);
                        anyChanged = true;
                    }
                }
                if (anyChanged)
                    ShowLookupService.Instance.SaveToFile();
            }

            _editSnapshot = null;
            _isDateEditMode = false;
            ShowsDataGrid.IsReadOnly = true;
        }

        /// <summary>
        /// Discards all pending edits and exits edit mode.
        /// </summary>
        public void CancelDateEdits()
        {
            if (!_isDateEditMode) return;

            // Cancel any active cell edit
            ShowsDataGrid.CancelEdit(DataGridEditingUnit.Cell);
            ShowsDataGrid.CancelEdit(DataGridEditingUnit.Row);

            // Restore from snapshot
            if (_editSnapshot != null)
            {
                foreach (var row in _dateRows)
                {
                    if (_editSnapshot.TryGetValue(row.Date, out var old))
                    {
                        row.Venue = old.Venue;
                        row.CityState = old.CityState;
                    }
                }
            }

            _editSnapshot = null;
            _isDateEditMode = false;
            ShowsDataGrid.IsReadOnly = true;
        }

        // ===== FILTERING =====

        /// <summary>
        /// Applies search text and type filter to the library grid.
        /// Called by ShellWindow when HeaderBar filter changes.
        /// </summary>
        public void ApplyFilter(string searchText, string typeFilter, string yearFilter = "All Years")
        {
            // Remember filter state for re-apply after ReloadLibrary
            _lastSearchText = searchText;
            _lastTypeFilter = typeFilter;
            _lastYearFilter = yearFilter;

            // Handle "Shows I Don't Have" mode
            if (typeFilter == "Shows I Don't Have")
            {
                if (!_isMissingShowsMode)
                {
                    // Exit other modes first
                    if (_isByDateMode)
                    {
                        if (_isDateEditMode) CancelDateEdits();
                        _isByDateMode = false;
                    }
                    _isMissingShowsMode = true;

                    // Build missing show rows on a background thread to avoid
                    // freezing the UI while comparing library against shows.json
                    _ = BuildMissingShowRowsAsync(searchText, yearFilter);
                    return;
                }

                // Already in missing shows mode — just re-filter the existing list
                ApplyMissingShowFilters(searchText, yearFilter);
                return;
            }

            // Exiting missing shows mode
            if (_isMissingShowsMode)
            {
                _isMissingShowsMode = false;
                SetAlbumColumns();
            }

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
                        IsHeadySearch(search) ? !string.IsNullOrEmpty(r.HeadyIcon)
                        : (ContainsIgnoreCase(r.Date, search)
                        || ContainsIgnoreCase(r.Venue, search)
                        || ContainsIgnoreCase(r.CityState, search)
                        || ContainsIgnoreCase(r.FromAlbum, search))
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

            // Exiting By Date mode — cancel any pending edits and restore album columns
            if (_isByDateMode)
            {
                if (_isDateEditMode)
                    CancelDateEdits();
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
            // Enrich venue/city from shows.json for rows that are missing venue info
            // (e.g., multi-date box set tracks that only have the album-level venue)
            foreach (var row in _dateRows)
            {
                var showInfo = ShowLookupService.Instance.GetShowByDate(row.Date);
                if (showInfo == null) continue;

                if (string.IsNullOrEmpty(row.Venue))
                    row.Venue = showInfo.Venue;

                if (string.IsNullOrEmpty(row.CityState))
                    row.CityState = showInfo.FormattedLocation;
            }

            _dateRows = _dateRows.OrderBy(r => r.Date).ToList();
        }

        // ===== SHOWS I DON'T HAVE MODE =====

        private void BuildMissingShowRows()
        {
            _missingShowRows.Clear();

            // Collect all dates the user owns (from library shows + multi-date albums)
            var ownedDates = new HashSet<string>();
            foreach (var show in _shows)
            {
                if (!string.IsNullOrEmpty(show.Date))
                    ownedDates.Add(show.Date);
                // Use ContainsDates only if already loaded — avoid triggering
                // the expensive lazy-load of track titles from disk (50+ seconds).
                // Mirrors the guard pattern used by LibraryShow.HeadyIcon.
                if (show._containsDatesLoaded && show._containsDates != null)
                {
                    foreach (var d in show._containsDates)
                        ownedDates.Add(d);
                }
            }

            // Get all dates from shows.json and find the ones not owned
            var allDates = ShowLookupService.Instance.GetAllDates();
            foreach (var date in allDates.OrderBy(d => d))
            {
                if (ownedDates.Contains(date)) continue;

                var showInfo = ShowLookupService.Instance.GetShowByDate(date);
                _missingShowRows.Add(new DateRow
                {
                    Date = date,
                    Venue = showInfo?.Venue ?? "",
                    CityState = showInfo?.FormattedLocation ?? "",
                    FromAlbum = "",
                    TrackCount = 0,
                    SourceShow = null!
                });
            }
        }

        /// <summary>
        /// Builds missing show rows on a background thread, then populates the grid on the UI thread.
        /// Shows a loading indicator while the computation runs.
        /// </summary>
        private async Task BuildMissingShowRowsAsync(string searchText, string yearFilter)
        {
            LoadingIndicator.Text = "Loading shows…";
            LoadingIndicator.Visibility = Visibility.Visible;
            ShowsDataGrid.ItemsSource = null;

            await Task.Run(() =>
            {
                BuildMissingShowRows();
            });

            // Back on UI thread — set up columns and populate grid
            SetMissingShowColumns();

            // Populate the year dropdown with years from missing shows
            var missingYears = _missingShowRows
                .Select(r => r.Date.Substring(0, 4))
                .Distinct()
                .OrderBy(y => y)
                .ToList();
            _shell.HeaderBar.PopulateYearFilter(missingYears);

            LoadingIndicator.Visibility = Visibility.Collapsed;

            ApplyMissingShowFilters(searchText, yearFilter);
        }

        /// <summary>
        /// Applies year and search filters to the already-built missing show rows.
        /// Runs synchronously on the UI thread (just in-memory filtering).
        /// </summary>
        private void ApplyMissingShowFilters(string searchText, string yearFilter)
        {
            // Compute total shows in scope (all shows.json dates for the selected year)
            var allDates = ShowLookupService.Instance.GetAllDates();
            if (yearFilter != "All Years")
            {
                TotalShowsInScope = allDates.Count(d => d.StartsWith(yearFilter));
            }
            else
            {
                TotalShowsInScope = allDates.Count;
            }

            // Start with all missing rows, then apply year and search filters
            IEnumerable<DateRow> rows = _missingShowRows;

            // Apply year filter
            if (yearFilter != "All Years")
            {
                rows = rows.Where(r => r.Date.StartsWith(yearFilter));
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                rows = rows.Where(r =>
                    IsHeadySearch(search) ? !string.IsNullOrEmpty(r.HeadyIcon)
                    : (ContainsIgnoreCase(r.Date, search)
                    || ContainsIgnoreCase(r.Venue, search)
                    || ContainsIgnoreCase(r.CityState, search))
                );
            }

            _filteredMissingRows = rows.ToList();
            ShowsDataGrid.ItemsSource = _filteredMissingRows;
            ConcertCountChanged?.Invoke(this, _filteredMissingRows.Count);
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

        /// <summary>
        /// Returns true if the search text is the special "heady" keyword.
        /// </summary>
        private static bool IsHeadySearch(string search)
        {
            return search.Equals("heady", StringComparison.OrdinalIgnoreCase)
                || search == "\u26A1";
        }

        private static bool MatchesSearch(LibraryShow show, string search)
        {
            // Special "heady" keyword: filter to shows with heady versions
            if (IsHeadySearch(search))
                return !string.IsNullOrEmpty(show.HeadyIcon);

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

        /// <summary>
        /// Reads custom Xiph Vorbis Comment fields (VENUE, CITYSTATE) from the first FLAC file
        /// in a folder and populates the LibraryShow. These fields are written by WriteMetadata
        /// during import/edit and allow Official Release metadata to round-trip through FLAC tags.
        /// </summary>
        private static void ReadCustomFieldsIntoShow(LibraryShow show, string folderPath)
        {
            try
            {
                // Find first audio file (FLAC or MP3)
                var firstAudio = Directory.GetFiles(folderPath, "*.flac").FirstOrDefault()
                              ?? Directory.GetFiles(folderPath, "*.mp3").FirstOrDefault();
                if (firstAudio == null) return;

                // PictureLazy: skip loading embedded artwork (not needed for custom field reads)
                using var tagFile = TagLib.File.Create(firstAudio, TagLib.ReadStyle.PictureLazy);

                string? venue = null;
                string? cityState = null;
                string? albumName = null;
                string? albumType = null;

                if (tagFile is TagLib.Flac.File flacFile)
                {
                    // FLAC: read from Xiph Vorbis Comments
                    var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                    if (xiph != null)
                    {
                        venue = xiph.GetFirstField("VENUE");
                        cityState = xiph.GetFirstField("CITYSTATE");
                        albumName = xiph.GetFirstField("ALBUMNAME");
                        albumType = xiph.GetFirstField("ALBUMTYPE");
                    }
                }
                else
                {
                    // MP3/other: read from ID3v2 TXXX frames
                    var id3v2 = (TagLib.Id3v2.Tag?)tagFile.GetTag(TagLib.TagTypes.Id3v2);
                    if (id3v2 != null)
                    {
                        var venueFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "VENUE", false);
                        if (venueFrame?.Text.Length > 0) venue = venueFrame.Text[0];

                        var cityFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "CITYSTATE", false);
                        if (cityFrame?.Text.Length > 0) cityState = cityFrame.Text[0];

                        var nameFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "ALBUMNAME", false);
                        if (nameFrame?.Text.Length > 0) albumName = nameFrame.Text[0];

                        var typeFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "ALBUMTYPE", false);
                        if (typeFrame?.Text.Length > 0) albumType = typeFrame.Text[0];
                    }
                }

                if (!string.IsNullOrEmpty(venue))
                    show.Venue = venue;

                if (!string.IsNullOrEmpty(cityState))
                {
                    show.Location = cityState;
                    var parts = cityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                    show.City = parts.Length > 0 ? parts[0] : "";
                    show.State = parts.Length > 1 ? parts[1] : "";
                }

                // Override folder-name-derived album name with tag value if present.
                // Fall back to standard ALBUM tag if the custom ALBUMNAME field is empty
                // (handles files tagged externally without DeadEditor's custom fields).
                // Skip ALBUM values that look like DeadEditor's computed "yyyy-MM-dd - Venue"
                // format, since those aren't real album names.
                if (string.IsNullOrEmpty(albumName))
                {
                    var standardAlbum = tagFile.Tag.Album;
                    if (!string.IsNullOrEmpty(standardAlbum) &&
                        !Regex.IsMatch(standardAlbum, @"^\d{4}-\d{2}-\d{2}\s*-"))
                    {
                        albumName = standardAlbum;
                    }
                }

                if (!string.IsNullOrEmpty(albumName))
                {
                    show.AlbumName = albumName;
                    if (!string.IsNullOrEmpty(show.OfficialRelease))
                        show.OfficialRelease = albumName;
                }

                // Override Type from ALBUMTYPE tag if present (source of truth over folder-based inference).
                // This handles the case where LibraryRootPath == OfficialReleasesPath and a show
                // gets scanned by the wrong loading method.
                if (!string.IsNullOrEmpty(albumType) && Enum.TryParse<AlbumType>(albumType, out var parsedType))
                {
                    show.Type = parsedType;
                    show.TypeFromTag = true;
                }

                // Read year from tag if not already set from folder name
                if (show.ReleaseYear == null && tagFile.Tag.Year > 0)
                {
                    show.ReleaseYear = (int)tagFile.Tag.Year;
                }

                // For official releases, extract the date from the first file's title tag
                // (already opened above). This avoids scanning ALL files with TagLib which
                // takes 30+ seconds for 900+ tracks. HeadyIcon uses _containsDatesLoaded
                // guard to avoid triggering the full lazy-load during rendering.
                if (show.Type == AlbumType.OfficialRelease && string.IsNullOrEmpty(show.Date))
                {
                    var firstTitle = tagFile.Tag.Title;
                    if (!string.IsNullOrEmpty(firstTitle))
                    {
                        var dateMatch = Regex.Match(firstTitle, @"\((\d{4}-\d{2}-\d{2})");
                        if (dateMatch.Success)
                            show.Date = dateMatch.Groups[1].Value;
                    }
                }

            }
            catch
            {
                // Skip if file can't be read
            }
        }

        // ===== LIBRARY LOADING =====

        /// <summary>
        /// Loads all albums from the universal folder structure:
        ///   {LibraryRoot}/{Artist}/{AlbumFolder}/
        /// Each subfolder under an artist folder is one album.
        /// Metadata (type, venue, date, etc.) comes from custom FLAC tags.
        /// Folder name is parsed as fallback for initial field values.
        /// </summary>
        private static void LoadAlbumsInto(List<LibraryShow> shows, string libraryRootPath)
        {
            foreach (var artistFolder in Directory.GetDirectories(libraryRootPath))
            {
                foreach (var albumFolder in Directory.GetDirectories(artistFolder))
                {
                    var flacCount = Directory.GetFiles(albumFolder, "*.flac").Length;
                    var mp3Count = Directory.GetFiles(albumFolder, "*.mp3").Length;

                    if (flacCount + mp3Count == 0) continue;

                    var folderName = Path.GetFileName(albumFolder);

                    // Parse folder name segments: "Artist - Date - Venue - City, ST - AlbumName"
                    // or "Artist - Year - AlbumName" for studio albums
                    var parts = folderName.Split(new[] { " - " }, StringSplitOptions.None);

                    string date = "";
                    string venue = "";
                    string location = "";
                    string albumName = "";

                    if (parts.Length >= 2)
                    {
                        // Skip first segment (artist) since it's the parent folder name
                        // Detect pattern: if second segment looks like a date (yyyy-MM-dd), it's a live recording
                        if (Regex.IsMatch(parts[1], @"^\d{4}-\d{2}-\d{2}$"))
                        {
                            // Live: Artist - Date - Venue - City, ST [- AlbumName]
                            date = parts[1];
                            if (parts.Length >= 3) venue = parts[2];
                            if (parts.Length >= 4) location = parts[3];
                            if (parts.Length >= 5) albumName = parts[4];
                        }
                        else if (Regex.IsMatch(parts[1], @"^\d{4}$"))
                        {
                            // Studio: Artist - Year - AlbumName
                            if (parts.Length >= 3) albumName = parts[2];
                        }
                        else
                        {
                            // Unknown pattern — treat remaining as album name
                            albumName = string.Join(" - ", parts.Skip(1));
                        }
                    }

                    var show = new LibraryShow
                    {
                        Date = date,
                        Venue = venue,
                        Location = location,
                        AlbumName = albumName,
                        TrackCount = flacCount + mp3Count,
                        FolderPath = albumFolder
                    };

                    // Parse city/state from location
                    if (!string.IsNullOrEmpty(location))
                    {
                        var locParts = location.Split(new[] { ", " }, 2, StringSplitOptions.None);
                        show.City = locParts.Length > 0 ? locParts[0] : "";
                        show.State = locParts.Length > 1 ? locParts[1] : "";
                    }

                    // Override folder-name-parsed values with custom FLAC tags (source of truth)
                    ReadCustomFieldsIntoShow(show, albumFolder);

                    shows.Add(show);
                }
            }
        }

        // ===== MULTI-FOLDER MERGE =====

        /// <summary>
        /// Merges official releases that share the same ALBUMNAME tag into single LibraryShow
        /// entries with combined FolderPaths and TrackCounts. This handles box sets split across
        /// multiple folders (e.g., "Get Shown the Light" in two series folders).
        /// Only operates on items at index >= startIndex (official releases portion of the list).
        /// </summary>
        private static void MergeOfficialReleasesByAlbumName(List<LibraryShow> shows, int startIndex)
        {
            // Group official releases by trimmed ALBUMNAME (case-insensitive)
            var officialShows = shows.Skip(startIndex).ToList();
            var groups = new Dictionary<string, List<LibraryShow>>(StringComparer.OrdinalIgnoreCase);

            foreach (var show in officialShows)
            {
                var key = (show.AlbumName ?? "").Trim();
                if (string.IsNullOrEmpty(key)) continue;

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<LibraryShow>();
                    groups[key] = list;
                }
                list.Add(show);
            }

            // Merge groups with 2+ entries
            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;

                // Pick the first as primary, merge others into it
                var primary = group[0];

                for (int i = 1; i < group.Count; i++)
                {
                    var other = group[i];

                    // Combine folder paths
                    foreach (var path in other.FolderPaths)
                    {
                        if (!primary.FolderPaths.Contains(path))
                            primary.FolderPaths.Add(path);
                    }

                    // Sum track counts
                    primary.TrackCount += other.TrackCount;

                    // Use earliest date
                    if (!string.IsNullOrEmpty(other.Date) &&
                        (string.IsNullOrEmpty(primary.Date) || string.Compare(other.Date, primary.Date, StringComparison.Ordinal) < 0))
                    {
                        primary.Date = other.Date;
                    }

                    // Merge ContainsDates if already loaded
                    if (other._containsDatesLoaded && other._containsDates != null)
                    {
                        if (!primary._containsDatesLoaded)
                        {
                            primary._containsDates = new List<string>();
                            primary._containsDatesLoaded = true;
                        }
                        foreach (var d in other._containsDates)
                        {
                            if (!primary._containsDates!.Contains(d))
                                primary._containsDates.Add(d);
                        }
                    }

                    // Fill in venue/city/state from whichever folder has them
                    if (string.IsNullOrEmpty(primary.Venue) && !string.IsNullOrEmpty(other.Venue))
                        primary.Venue = other.Venue;
                    if (string.IsNullOrEmpty(primary.Location) && !string.IsNullOrEmpty(other.Location))
                    {
                        primary.Location = other.Location;
                        primary.City = other.City;
                        primary.State = other.State;
                    }

                    // Keep OfficialRelease name from tag
                    if (string.IsNullOrEmpty(primary.OfficialRelease) && !string.IsNullOrEmpty(other.OfficialRelease))
                        primary.OfficialRelease = other.OfficialRelease;

                    // Remove the merged show from the list
                    shows.Remove(other);
                }

                // Sort ContainsDates after merge
                if (primary._containsDatesLoaded && primary._containsDates != null)
                    primary._containsDates.Sort();
            }
        }

        // ===== INLINE EDITING (By Date mode) =====

        private void ShowsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Edits are batched — Save button persists all at once
        }

        private static void PersistDateRowToShows(DateRow row, bool save = true)
        {
            if (string.IsNullOrEmpty(row.Date)) return;

            // Parse CityState into city + state/country
            var existing = ShowLookupService.Instance.GetShowByDate(row.Date);
            string city = "";
            string state = "";
            string country = existing?.Country ?? "US";

            if (!string.IsNullOrEmpty(row.CityState))
            {
                var parts = row.CityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                city = parts[0].Trim();
                if (parts.Length > 1)
                {
                    var code = parts[1].Trim();
                    if (code.Length == 2 && code == code.ToUpper())
                    {
                        // 2-letter uppercase code — could be US state or country
                        if (existing != null && existing.Country != "US")
                        {
                            // Non-US show: treat as country code, keep existing state
                            country = code;
                            state = existing.State;
                        }
                        else
                        {
                            state = code;
                            country = "US";
                        }
                    }
                    else
                    {
                        // Longer code or mixed case — treat as state, preserve country
                        state = code;
                    }
                }
            }

            ShowLookupService.Instance.UpdateShow(row.Date, row.Venue, city, state, country);
            if (save)
                ShowLookupService.Instance.SaveToFile();
        }

        // ===== NAVIGATION =====

        private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // In edit mode, double-click on editable columns starts inline editing
            if (_isDateEditMode && ShowsDataGrid.CurrentCell.Column != null
                && !ShowsDataGrid.CurrentCell.Column.IsReadOnly)
            {
                return;
            }

            // "Shows I Don't Have" mode — open Jerrybase for the date
            if (_isMissingShowsMode && ShowsDataGrid.SelectedItem is DateRow missingRow)
            {
                if (!string.IsNullOrEmpty(missingRow.Date))
                {
                    var url = $"https://www.jerrybase.com/default/date/{missingRow.Date}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                return;
            }

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
