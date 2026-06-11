using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace DeadEditor
{
    public partial class AlbumDetailView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibraryShow _show;
        private readonly MetadataService _metadataService;
        private List<TrackInfo> _tracks = new();
        private ObservableCollection<ConcertViewItem> _concertViewItems = new();
        private bool _isMultiNight;
        private bool _isFlatSorted; // True when user clicked a column header to sort flat

        public string AlbumName =>
            !string.IsNullOrEmpty(_show.OfficialRelease) ? _show.OfficialRelease
            : !string.IsNullOrEmpty(_show.AlbumName) ? _show.AlbumName
            : _show.Venue;

        public AlbumDetailView(ShellWindow shell, LibraryShow show)
        {
            InitializeComponent();
            _shell = shell;
            _show = show;
            _metadataService = new MetadataService();
        }

        private async void AlbumDetailView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAlbumData();

            // Show loading indicator while reading tags from disk
            TracksLoadingIndicator.Visibility = Visibility.Visible;
            TracksDataGrid.Visibility = Visibility.Collapsed;

            // Run artwork + track I/O concurrently on background thread
            BitmapImage? albumArt = null;
            var tracks = await Task.Run(() =>
            {
                albumArt = LoadAlbumArtBackground();
                return LoadTracksFromDisk();
            });

            // Back on UI thread — apply artwork and bind track results
            ApplyAlbumArt(albumArt);
            TracksLoadingIndicator.Visibility = Visibility.Collapsed;
            TracksDataGrid.Visibility = Visibility.Visible;
            BindTracksToUI(tracks);
        }

        private void LoadAlbumData()
        {
            // Determine the display name: OfficialRelease > AlbumName > Venue
            var displayName = !string.IsNullOrEmpty(_show.OfficialRelease) ? _show.OfficialRelease
                            : !string.IsNullOrEmpty(_show.AlbumName) ? _show.AlbumName
                            : _show.Venue;

            AlbumNameDetailText.Text = displayName;

            // Set metadata labels below album name
            if (_show.Type == AlbumType.OfficialRelease)
            {
                VenueText.Text = !string.IsNullOrEmpty(_show.Venue) ? _show.Venue : "";
                LocationText.Text = _show.ReleaseYear.HasValue ? $"Released {_show.ReleaseYear}" : "";
                DateText.Text = "";
            }
            else
            {
                VenueText.Text = _show.Venue;
                LocationText.Text = _show.Location;
                DateText.Text = _show.Date;
            }

        }


        /// <summary>
        /// Loads album artwork from disk. Runs on a background thread — no UI access.
        /// Returns a frozen BitmapImage usable on the UI thread, or null.
        /// </summary>
        private BitmapImage? LoadAlbumArtBackground()
        {
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                // Try cover.jpg, then folder.jpg
                var artworkPath = Path.Combine(folder, "cover.jpg");
                if (!File.Exists(artworkPath))
                    artworkPath = Path.Combine(folder, "folder.jpg");

                if (File.Exists(artworkPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                    catch { }
                }

                // Fallback: embedded art in first audio file
                var audioFiles = Directory.GetFiles(folder, "*.flac")
                    .Concat(Directory.GetFiles(folder, "*.mp3"))
                    .ToArray();
                if (audioFiles.Length > 0)
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(audioFiles[0]);
                        if (tagFile.Tag.Pictures.Length > 0)
                        {
                            var pic = tagFile.Tag.Pictures[0];
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = new MemoryStream(pic.Data.Data);
                            bitmap.EndInit();
                            bitmap.Freeze();
                            return bitmap;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// Applies a pre-loaded album art bitmap to the UI. Must run on the UI thread.
        /// </summary>
        private void ApplyAlbumArt(BitmapImage? bitmap)
        {
            if (bitmap != null)
            {
                AlbumArtImage.Source = bitmap;
                AlbumArtImage.Visibility = Visibility.Visible;
                ArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                ArtworkPlaceholder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Reads all audio file tags from disk. Runs on a background thread — no UI access.
        /// </summary>
        private List<TrackInfo> LoadTracksFromDisk()
        {
            var tracks = new List<TrackInfo>();

            // Use FolderPaths (plural) to support multi-folder albums
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                var audioFiles = Directory.GetFiles(folder, "*.flac")
                    .Concat(Directory.GetFiles(folder, "*.mp3"))
                    .OrderBy(f => f)
                    .ToList();

                foreach (var file in audioFiles)
                {
                    try
                    {
                        // Average: read audio properties (Duration). PictureLazy: defer embedded artwork.
                        using (var tagFile = TagLib.File.Create(file, TagLib.ReadStyle.Average | TagLib.ReadStyle.PictureLazy))
                        {
                            var title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file);

                            // Extract date from title (yyyy-MM-dd format)
                            var dateMatch = Regex.Match(title, @"(\d{4}-\d{2}-\d{2})");
                            var trackDate = dateMatch.Success ? dateMatch.Groups[1].Value : null;

                            // Parse SongName from the raw title: strip date suffix and segue marker
                            var songName = title;
                            bool hasSegue = false;

                            // Detect and strip segue marker
                            if (songName.Contains(" >"))
                            {
                                hasSegue = true;
                                songName = Regex.Replace(songName, @"\s*>\s*", "").Trim();
                            }

                            // Strip date suffix like "(1971-02-19)"
                            songName = Regex.Replace(songName, @"\s*\(\d{4}-\d{2}-\d{2}\)\s*$", "").Trim();

                            var track = new TrackInfo
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                TrackNumber = tagFile.Tag.Track > 0 ? (int)tagFile.Tag.Track : tracks.Count + 1,
                                SongName = songName,
                                RawTitle = title,  // Store full FLAC title as-is (includes date suffix)
                                Duration = tagFile.Properties.Duration.ToString(@"m\:ss"),
                                TrackDate = trackDate,
                                Segue = hasSegue
                            };

                            tracks.Add(track);
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }

            // Populate TrackDate for tracks that don't have embedded dates
            var showDate = _show.Date; // _show fields are read-only here, safe to access
            foreach (var track in tracks)
            {
                if (string.IsNullOrEmpty(track.TrackDate) && !string.IsNullOrEmpty(showDate))
                {
                    track.TrackDate = showDate;
                }
            }

            // Sort tracks
            var distinctDates = tracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .Select(t => t.TrackDate)
                .Distinct()
                .Count();

            if (distinctDates > 1)
            {
                // Multi-night: Sort by date first, then track number
                tracks = tracks
                    .OrderBy(t => t.TrackDate ?? "")
                    .ThenBy(t => t.TrackNumber)
                    .ToList();
            }
            else
            {
                // Single-night: Sort by track number only
                tracks = tracks.OrderBy(t => t.TrackNumber).ToList();
            }
            return tracks;
        }

        /// <summary>
        /// Binds loaded track data to the UI. Must run on the UI thread.
        /// </summary>
        private void BindTracksToUI(List<TrackInfo> tracks)
        {
            _tracks = tracks;

            // Detect multi-night albums by counting distinct dates
            var distinctDates = _tracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .Select(t => t.TrackDate)
                .Distinct()
                .Count();

            _isMultiNight = distinctDates > 1;
            _isFlatSorted = false;

            // Build view based on date count
            if (_isMultiNight)
            {
                // Multi-night: Build collapsible sections
                BuildCollapsibleConcertView();
                TracksDataGrid.ItemsSource = _concertViewItems;

                // Show expand/collapse buttons for multi-night albums
                ExpandAllButton.Visibility = Visibility.Visible;
                CollapseAllButton.Visibility = Visibility.Visible;
            }
            else
            {
                // Single-night: Bind directly to tracks (no headers)
                TracksDataGrid.ItemsSource = _tracks;
            }

            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";

            // Count heady versions in this album (in-memory lookups, fast)
            var heady = HeadyVersionService.Instance;
            int headyCount = 0;
            foreach (var t in _tracks)
            {
                var match = (!string.IsNullOrEmpty(t.SongName) && !string.IsNullOrEmpty(t.TrackDate))
                    ? heady.GetHeadyVersion(t.SongName, t.TrackDate) : null;
                if (match != null) headyCount++;
            }

            if (headyCount > 0)
            {
                HeadyCountText.Text = headyCount == 1
                    ? "\u26A1 1 heady version"
                    : $"\u26A1 {headyCount} heady versions";
                HeadyCountText.Visibility = Visibility.Visible;
            }
        }

        private void BuildCollapsibleConcertView()
        {
            _concertViewItems.Clear();

            // Group tracks by date
            var tracksByDate = _tracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .GroupBy(t => t.TrackDate)
                .OrderBy(g => g.Key);

            bool isFirstSection = true;

            foreach (var dateGroup in tracksByDate)
            {
                var date = dateGroup.Key;
                var dateTracks = dateGroup.ToList();

                // Lookup venue info from shows.json
                var showInfo = ShowLookupService.Instance.GetShowByDate(date);
                var venue = showInfo?.Venue ?? "";
                var location = showInfo?.FormattedLocation ?? "";

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
        }

        private void ToggleDateSection(DateHeaderItem header)
        {
            RestoreGroupedView();

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

                // Expand this section
                var headerIndex = _concertViewItems.IndexOf(header);
                var dateTracks = _tracks
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

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            RestoreGroupedView();
            var allHeaders = _concertViewItems.OfType<DateHeaderItem>().ToList();
            foreach (var header in allHeaders)
            {
                if (!header.IsExpanded)
                {
                    var headerIndex = _concertViewItems.IndexOf(header);
                    var dateTracks = _tracks.Where(t => t.TrackDate == header.Date).ToList();

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
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            RestoreGroupedView();
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
        }

        // ===== COLUMN SORTING =====

        private void TracksDataGrid_Sorting(object sender, System.Windows.Controls.DataGridSortingEventArgs e)
        {
            e.Handled = true; // We handle sorting ourselves

            var sortPath = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(sortPath)) return;

            // Toggle direction
            var direction = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending
                ? System.ComponentModel.ListSortDirection.Descending
                : System.ComponentModel.ListSortDirection.Ascending;

            // Clear sort indicators on all columns
            foreach (var col in TracksDataGrid.Columns)
                col.SortDirection = null;
            e.Column.SortDirection = direction;

            // Sort flat track list
            IOrderedEnumerable<TrackInfo> sorted = sortPath switch
            {
                "TrackNumber" => direction == System.ComponentModel.ListSortDirection.Ascending
                    ? _tracks.OrderBy(t => t.TrackNumber)
                    : _tracks.OrderByDescending(t => t.TrackNumber),
                "Title" => direction == System.ComponentModel.ListSortDirection.Ascending
                    ? _tracks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    : _tracks.OrderByDescending(t => t.Title, StringComparer.OrdinalIgnoreCase),
                "Duration" => direction == System.ComponentModel.ListSortDirection.Ascending
                    ? _tracks.OrderBy(t => t.Duration)
                    : _tracks.OrderByDescending(t => t.Duration),
                _ => _tracks.OrderBy(t => t.TrackNumber)
            };

            // Switch to flat view (no date group headers)
            _isFlatSorted = true;
            TracksDataGrid.ItemsSource = sorted.ToList();
        }

        /// <summary>
        /// Restores the grouped/collapsible date view after flat sorting.
        /// Called by Expand All, Collapse All, or date header clicks.
        /// </summary>
        private void RestoreGroupedView()
        {
            if (!_isFlatSorted) return;
            _isFlatSorted = false;

            // Clear sort indicators
            foreach (var col in TracksDataGrid.Columns)
                col.SortDirection = null;

            if (_isMultiNight)
            {
                BuildCollapsibleConcertView();
                TracksDataGrid.ItemsSource = _concertViewItems;
            }
            else
            {
                TracksDataGrid.ItemsSource = _tracks;
            }
        }

        private void AddAllTracksButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string date)
                return;

            // Get all tracks for this date
            var tracksForDate = _tracks
                .Where(t => t.TrackDate == date)
                .ToList();

            if (tracksForDate.Any())
            {
                var playlist = App.PlaybackService.Playlist;
                foreach (var track in tracksForDate)
                {
                    if (!playlist.Contains(track))
                    {
                        playlist.Add(track);
                    }
                }
            }
        }

        private void TracksDataGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Single-click handler: Only handle DateHeaderItem clicks (toggle expand/collapse)
            var selectedItem = TracksDataGrid.SelectedItem;

            if (selectedItem is DateHeaderItem header)
            {
                // Clicked on header - toggle expansion
                ToggleDateSection(header);
            }
        }

        private void TracksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click handler: Play tracks or toggle headers
            var selectedItem = TracksDataGrid.SelectedItem;
            TrackInfo? track = null;

            if (selectedItem is TrackInfo directTrack)
            {
                track = directTrack;
            }
            else if (selectedItem is TrackViewItem trackViewItem)
            {
                track = trackViewItem.Track;
            }
            else if (selectedItem is DateHeaderItem header)
            {
                ToggleDateSection(header);
                return;
            }

            if (track != null)
            {
                PlayNow(track);
            }
        }

        // ===== CONTEXT MENU =====

        private void TracksDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Get the item under the cursor
            var hit = VisualTreeHelper.HitTest(TracksDataGrid, e.GetPosition(TracksDataGrid));
            if (hit == null) return;

            // Walk up the visual tree to find the DataGridRow
            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not System.Windows.Controls.DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not System.Windows.Controls.DataGridRow row) return;

            // Don't show context menu on date header rows
            if (row.Item is DateHeaderItem) return;

            // Resolve the TrackInfo from the row item
            TrackInfo? clickedTrack = row.Item switch
            {
                TrackInfo t => t,
                TrackViewItem tvi => tvi.Track,
                _ => null
            };
            if (clickedTrack == null) return;

            // Build dark-themed context menu
            var menu = new System.Windows.Controls.ContextMenu
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2)
            };

            var menuItemStyle = new Style(typeof(System.Windows.Controls.MenuItem));
            menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.ForegroundProperty,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0))));
            menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.PaddingProperty, new Thickness(8, 6, 20, 6)));
            menuItemStyle.Setters.Add(new Setter(System.Windows.Controls.MenuItem.FontSizeProperty, 14.0));

            var playNowItem = new System.Windows.Controls.MenuItem { Header = "▶  Play Now", Style = menuItemStyle };
            playNowItem.Click += (s, args) => PlayNow(clickedTrack);
            menu.Items.Add(playNowItem);

            var addItem = new System.Windows.Controls.MenuItem { Header = "＋  Add to Playlist", Style = menuItemStyle };
            addItem.Click += (s, args) =>
            {
                int added = AddToPlaylistWithSegueChain(clickedTrack);
                ShowTemporaryStatus($"{added} track{(added == 1 ? "" : "s")} added to playlist");
            };
            menu.Items.Add(addItem);

            // "Add Selected" — only when multiple tracks are selected
            var selectedTracks = GetSelectedTracks();
            if (selectedTracks.Count > 1)
            {
                var addSelectedItem = new System.Windows.Controls.MenuItem
                {
                    Header = $"＋  Add {selectedTracks.Count} Selected to Playlist",
                    Style = menuItemStyle
                };
                addSelectedItem.Click += (s, args) =>
                {
                    int added = 0;
                    var playlist = App.PlaybackService.Playlist;
                    foreach (var track in selectedTracks)
                    {
                        if (!playlist.Contains(track))
                        {
                            playlist.Add(track);
                            added++;
                        }
                    }
                    ShowTemporaryStatus($"{added} track{(added == 1 ? "" : "s")} added to playlist");
                };
                menu.Items.Add(addSelectedItem);
            }

            // Track Info
            var trackInfoItem = new System.Windows.Controls.MenuItem { Header = "\U0001F4C4  Track Info", Style = menuItemStyle };
            trackInfoItem.Click += (s, args) =>
            {
                var dialog = new TrackInfoDialog(
                    clickedTrack,
                    artist: null,
                    album: _show?.AlbumName);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            };
            menu.Items.Add(trackInfoItem);

            // Separator before destructive action
            menu.Items.Add(new System.Windows.Controls.Separator
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                Margin = new Thickness(4, 2, 4, 2)
            });

            var deleteItem = new System.Windows.Controls.MenuItem { Header = "🗑  Delete Track", Style = menuItemStyle };
            deleteItem.Click += (s, args) => DeleteTrack(clickedTrack);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        // ===== DELETE TRACK =====

        private void DeleteTrack(TrackInfo track)
        {
            var result = System.Windows.MessageBox.Show(
                $"Delete track {track.TrackNumber} '{track.Title}'?\n\nThis will send the file to the Recycle Bin.",
                "Delete Track",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK) return;

            try
            {
                FileSystem.DeleteFile(
                    track.FilePath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                App.Alerts.Notify($"Could not delete file:\n{ex.Message}", AlertSeverity.Error, "Error");
                return;
            }

            // Remove from playlist if present
            App.PlaybackService.Playlist.Remove(track);

            // Remove from internal track list
            _tracks.Remove(track);

            // Update the view
            if (_tracks.Count == 0)
            {
                // Last track deleted — go back to library
                _shell.Navigation.GoBack();
                return;
            }

            if (_isMultiNight && !_isFlatSorted)
            {
                // Remove the TrackViewItem from the observable collection
                var viewItem = _concertViewItems.OfType<TrackViewItem>().FirstOrDefault(t => t.Track == track);
                if (viewItem != null)
                {
                    var header = viewItem.ParentHeader;
                    _concertViewItems.Remove(viewItem);

                    // Update header track count
                    if (header != null)
                    {
                        header.TrackCount = _tracks.Count(t => t.TrackDate == header.Date);

                        // If date section is now empty, remove the header too
                        if (header.TrackCount == 0)
                            _concertViewItems.Remove(header);
                    }
                }
            }
            else if (_isFlatSorted)
            {
                // Flat sorted view — rebuild from current _tracks
                var currentSource = TracksDataGrid.ItemsSource as List<TrackInfo>;
                if (currentSource != null)
                {
                    currentSource.Remove(track);
                    TracksDataGrid.ItemsSource = null;
                    TracksDataGrid.ItemsSource = currentSource;
                }
            }
            else
            {
                // Single-night view — rebind
                TracksDataGrid.ItemsSource = null;
                TracksDataGrid.ItemsSource = _tracks;
            }

            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";
        }

        // ===== PLAYLIST HELPERS =====

        /// <summary>
        /// Returns the segue chain starting from the given track.
        /// If the track has Segue=true, walks forward through _tracks
        /// adding consecutive tracks until a non-segue track is reached (inclusive).
        /// </summary>
        private List<TrackInfo> GetSegueChain(TrackInfo track)
        {
            var chain = new List<TrackInfo> { track };
            int startIndex = _tracks.IndexOf(track);

            if (startIndex >= 0 && track.Segue)
            {
                for (int i = startIndex + 1; i < _tracks.Count; i++)
                {
                    chain.Add(_tracks[i]);
                    if (!_tracks[i].Segue) break;
                }
            }

            return chain;
        }

        /// <summary>
        /// Adds a track and its segue chain to the playlist without playing.
        /// Returns the number of tracks actually added (excludes duplicates).
        /// </summary>
        private int AddToPlaylistWithSegueChain(TrackInfo track)
        {
            var chain = GetSegueChain(track);
            var playlist = App.PlaybackService.Playlist;
            int added = 0;

            foreach (var chainTrack in chain)
            {
                if (!playlist.Contains(chainTrack))
                {
                    playlist.Add(chainTrack);
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// Adds track + segue chain to playlist and starts playing immediately.
        /// </summary>
        private void PlayNow(TrackInfo track)
        {
            AddToPlaylistWithSegueChain(track);
            App.PlaybackService.Play(track);
        }

        /// <summary>
        /// Returns all selected TrackInfo items from the DataGrid (skips headers).
        /// </summary>
        private List<TrackInfo> GetSelectedTracks()
        {
            var tracks = new List<TrackInfo>();
            foreach (var item in TracksDataGrid.SelectedItems)
            {
                if (item is TrackInfo t)
                    tracks.Add(t);
                else if (item is TrackViewItem tvi)
                    tracks.Add(tvi.Track);
            }
            return tracks;
        }

        /// <summary>
        /// Shows a temporary status message in the track count area, then reverts.
        /// </summary>
        private async void ShowTemporaryStatus(string message)
        {
            var original = TrackCountText.Text;
            TrackCountText.Text = message;
            TrackCountText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC));
            await System.Threading.Tasks.Task.Delay(2000);
            TrackCountText.Text = original;
            TrackCountText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        }

        private void HeadyIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock tb && tb.Tag is string url && !string.IsNullOrEmpty(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                e.Handled = true;
            }
        }

        public void NavigateBack()
        {
            _shell.Navigation.GoBack();
        }
    }
}
