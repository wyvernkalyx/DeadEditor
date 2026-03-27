using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DeadEditor
{
    public partial class AlbumDetailView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibraryShow _show;
        private readonly MetadataService _metadataService;
        private List<TrackInfo> _tracks = new();
        private ObservableCollection<ConcertViewItem> _concertViewItems = new();

        public string AlbumName => _show.Type == AlbumType.OfficialRelease ? _show.AlbumName : _show.Venue;

        public AlbumDetailView(ShellWindow shell, LibraryShow show)
        {
            InitializeComponent();
            _shell = shell;
            _show = show;
            _metadataService = new MetadataService();
        }

        private void AlbumDetailView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAlbumData();
            LoadAlbumArt();
            LoadTracks();
        }

        private void LoadAlbumData()
        {
            // Set metadata labels
            if (_show.Type == AlbumType.OfficialRelease)
            {
                VenueText.Text = _show.AlbumName;
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

        private void LoadAlbumArt()
        {
            // Use FolderPaths (plural) to support multi-folder albums
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };

            BitmapImage? bitmap = null;

            // Try each folder in order until we find artwork
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                // Try cover.jpg
                var artworkPath = Path.Combine(folder, "cover.jpg");
                if (!File.Exists(artworkPath))
                {
                    // Try folder.jpg
                    artworkPath = Path.Combine(folder, "folder.jpg");
                }

                if (File.Exists(artworkPath))
                {
                    try
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                        bitmap.EndInit();
                        break; // Found artwork, stop searching
                    }
                    catch
                    {
                        bitmap = null;
                    }
                }

                // If no file-based artwork, try embedded APIC in first FLAC
                if (bitmap == null)
                {
                    var flacFiles = Directory.GetFiles(folder, "*.flac");
                    if (flacFiles.Length > 0)
                    {
                        try
                        {
                            using var tagFile = TagLib.File.Create(flacFiles[0]);
                            if (tagFile.Tag.Pictures.Length > 0)
                            {
                                var pic = tagFile.Tag.Pictures[0];
                                bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = new System.IO.MemoryStream(pic.Data.Data);
                                bitmap.EndInit();
                                break; // Found embedded artwork, stop searching
                            }
                        }
                        catch
                        {
                            bitmap = null;
                        }
                    }
                }

                if (bitmap != null)
                    break; // Found artwork in this folder, stop searching other folders
            }

            // Set the image or show placeholder
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

        private void LoadTracks()
        {
            _tracks.Clear();

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
                        using (var tagFile = TagLib.File.Create(file))
                        {
                            var title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file);

                            // Extract date from title (yyyy-MM-dd format)
                            var dateMatch = Regex.Match(title, @"(\d{4}-\d{2}-\d{2})");
                            var trackDate = dateMatch.Success ? dateMatch.Groups[1].Value : null;

                            var track = new TrackInfo
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                TrackNumber = tagFile.Tag.Track > 0 ? (int)tagFile.Tag.Track : _tracks.Count + 1,
                                RawTitle = title,  // Store full FLAC title as-is (includes date suffix)
                                Duration = tagFile.Properties.Duration.ToString(@"m\:ss"),
                                TrackDate = trackDate
                            };

                            _tracks.Add(track);
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }

            // Populate TrackDate for tracks that don't have embedded dates
            foreach (var track in _tracks)
            {
                if (string.IsNullOrEmpty(track.TrackDate) && !string.IsNullOrEmpty(_show.Date))
                {
                    track.TrackDate = _show.Date;
                }
            }

            // Detect multi-night albums by counting distinct dates
            var distinctDates = _tracks
                .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                .Select(t => t.TrackDate)
                .Distinct()
                .Count();

            // Sort tracks
            if (distinctDates > 1)
            {
                // Multi-night: Sort by date first, then track number
                _tracks = _tracks
                    .OrderBy(t => t.TrackDate ?? "")
                    .ThenBy(t => t.TrackNumber)
                    .ToList();
            }
            else
            {
                // Single-night: Sort by track number only
                _tracks = _tracks.OrderBy(t => t.TrackNumber).ToList();
            }

            // Build view based on date count
            if (distinctDates > 1)
            {
                // Multi-night: Build collapsible sections
                BuildCollapsibleConcertView();
                TracksDataGrid.ItemsSource = _concertViewItems;
            }
            else
            {
                // Single-night: Bind directly to tracks (no headers)
                TracksDataGrid.ItemsSource = _tracks;
            }

            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";
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

                // Create date header
                var header = new DateHeaderItem
                {
                    Date = date,
                    Venue = "", // Could extract from folder name if needed
                    Location = "",
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
                // Clicked on header - toggle expansion (also works with double-click)
                ToggleDateSection(header);
                return;
            }

            if (track != null)
            {
                // Add track to playlist and play
                var playlist = App.PlaybackService.Playlist;

                // Check if already in playlist
                if (!playlist.Contains(track))
                {
                    playlist.Add(track);
                }

                // Play the track
                App.PlaybackService.Play(track);
            }
        }

        public void NavigateBack()
        {
            _shell.Navigation.GoBack();
        }
    }
}
