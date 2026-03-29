using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;

namespace DeadEditor
{
    /// <summary>
    /// ImportView - Concert Import UserControl for the Shell
    ///
    /// Provides the full import pipeline:
    ///   Select Folder → Read → MusicBrainz → Normalize → Renumber → Write / Import to Library
    ///
    /// State is preserved when the user switches away to Library / Settings and returns.
    /// Fires ImportCompleted when a concert is successfully imported so ShellWindow can
    /// trigger a library refresh.
    /// </summary>
    public partial class ImportView : WpfUserControl
    {
        // ===== EVENTS =====

        /// <summary>Fired after a successful Import to Library. Carries the destination path.</summary>
        public event EventHandler<string>? ImportCompleted;

        // ===== SERVICES =====

        private readonly MetadataService _metadataService;
        private readonly NormalizationService _normalizationService;
        private readonly LibraryImportService _libraryImportService;
        private readonly MusicBrainzService _musicBrainzService;
        private LibrarySettings _librarySettings;

        // ===== DATA =====

        private ObservableCollection<TrackInfoViewModel> _tracks = new();
        private AlbumInfo? _albumInfo;
        private string? _currentFolderPath;
        private bool _isUpdating = false;
        private TaskCompletionSource<bool>? _notificationResult;

        // ===== PLAYBACK =====

        // ===== CONSTRUCTOR =====

        public ImportView()
        {
            InitializeComponent();

            _metadataService = new MetadataService();
            _normalizationService = new NormalizationService();
            _librarySettings = LibrarySettings.Load();
            _libraryImportService = new LibraryImportService(_metadataService);
            _musicBrainzService = new MusicBrainzService("asa4wLQhwJ", _librarySettings);

            TracksDataGrid.ItemsSource = _tracks;
        }

        // ===== PUBLIC API =====

        /// <summary>
        /// Called by ShellWindow's HeaderBar folder picker so the folder path
        /// can also appear in the header bar. Loads the folder.
        /// </summary>
        public void LoadFolderFromPath(string folderPath)
        {
            _currentFolderPath = folderPath;
            _ = LoadFolderAsync(folderPath);
        }

        /// <summary>Called by the Header Bar "Select Folder…" button.</summary>
        public void BrowseForFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing audio files (FLAC/MP3)"
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _currentFolderPath = dlg.SelectedPath;
                _ = LoadFolderAsync(dlg.SelectedPath);

                // Notify header bar about the new path
                FolderPathSelected?.Invoke(this, dlg.SelectedPath);
            }
        }

        /// <summary>
        /// Raised when the user picks a folder so the HeaderBar can display the path.
        /// </summary>
        public event EventHandler<string>? FolderPathSelected;

        /// <summary>True if tracks are loaded but not yet imported to library.</summary>
        public bool HasUnsavedWork => _tracks.Count > 0;

        // ===== FOLDER LOADING =====

        private async Task LoadFolderAsync(string folderPath)
        {
            try
            {
                StatusTextBlock.Text = "Reading files...";

                var trackList = _metadataService.ReadFolder(folderPath, editMode: false);

                if (trackList.Count == 0)
                {
                    await ShowNotificationAsync("No Files",
                        "No audio files (FLAC/MP3) found in the selected folder.");
                    StatusTextBlock.Text = "No audio files found";
                    return;
                }

                // Read album info from folder / FLAC tags
                _albumInfo = _metadataService.ReadAlbumInfo(folderPath, trackList);

                // Set album date on each track for display fallback (playlist, etc.)
                if (!string.IsNullOrEmpty(_albumInfo.AlbumDate))
                {
                    foreach (var track in trackList)
                    {
                        track.AlbumDate = _albumInfo.AlbumDate;
                    }
                }

                // Multi-night sort logic
                var distinctDates = trackList
                    .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                    .Select(t => t.TrackDate)
                    .Distinct()
                    .Count();

                bool hasDiscNumbers = trackList.Any(t => t.DiscNumber > 1);

                if (distinctDates > 1)
                {
                    trackList = trackList
                        .OrderBy(t => string.IsNullOrEmpty(t.TrackDate) ? "9999-99-99" : t.TrackDate)
                        .ThenBy(t => t.DiscNumber)
                        .ThenBy(t => t.TrackNumber)
                        .ToList();
                }
                else if (hasDiscNumbers)
                {
                    trackList = trackList
                        .OrderBy(t => t.DiscNumber)
                        .ThenBy(t => t.TrackNumber)
                        .ToList();
                }
                else
                {
                    trackList = trackList.OrderBy(t => t.TrackNumber).ToList();
                }

                // Populate ViewModels
                _tracks.Clear();
                foreach (var track in trackList)
                {
                    _tracks.Add(new TrackInfoViewModel(track, _albumInfo, isEditMode: false));
                }

                // Refresh all bound UI fields
                RefreshUI();

                StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error loading folder: {ex.Message}");
                StatusTextBlock.Text = "Error loading folder";
            }
        }

        private void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            BrowseForFolder();
        }

        // ===== REFRESH / CLEAR =====

        private void RefreshUI()
        {
            _isUpdating = true;

            if (_albumInfo != null)
            {
                ArtistTextBox.Text = _albumInfo.Artist ?? "";
                AlbumDateTextBox.Text = _albumInfo.AlbumDate ?? "";
                VenueTextBox.Text = _albumInfo.Venue ?? "";
                CityStateTextBox.Text = _albumInfo.CityState ?? "";
                AlbumNameTextBox.Text = _albumInfo.AlbumName ?? "";
                YearTextBox.Text = _albumInfo.Year ?? "";

                AlbumTypeComboBox.SelectedIndex = _albumInfo.Type switch
                {
                    AlbumType.AudienceRecording => 1,
                    AlbumType.OfficialRelease => 2,
                    _ => 0
                };

                UpdateArtworkDisplay();
                ViewInfoButton.IsEnabled = !string.IsNullOrEmpty(_albumInfo.InfoFileContent);
            }

            UpdateAlbumPreview();
            UpdateAllTrackDisplayTitles();

            _isUpdating = false;
        }

        private void ClearView()
        {
            _isUpdating = true;

            _tracks.Clear();
            _albumInfo = null;
            _currentFolderPath = null;
            ArtistTextBox.Text = "";
            AlbumDateTextBox.Text = "";
            VenueTextBox.Text = "";
            CityStateTextBox.Text = "";
            AlbumNameTextBox.Text = "";
            YearTextBox.Text = "";
            AlbumTypeComboBox.SelectedIndex = 0;
            FolderPreviewTextBlock.Text = "";
            WritePathTextBlock.Text = "";
            ViewInfoButton.IsEnabled = false;
            ImportButton.IsEnabled = true;
            WriteButton.IsEnabled = true;
            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Ready — select a folder to begin";

            _isUpdating = false;
        }

        // ===== ALBUM INFO BAR =====

        private void AlbumInfo_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            _albumInfo.Artist = ArtistTextBox.Text;
            _albumInfo.AlbumDate = AlbumDateTextBox.Text;
            _albumInfo.Venue = VenueTextBox.Text;
            _albumInfo.CityState = CityStateTextBox.Text;
            _albumInfo.AlbumName = AlbumNameTextBox.Text;
            _albumInfo.Year = YearTextBox.Text;
            _albumInfo.IsModified = true;

            // Auto-detect album type
            if (!string.IsNullOrWhiteSpace(_albumInfo.AlbumName))
            {
                _albumInfo.Type = AlbumType.OfficialRelease;
                _isUpdating = true;
                AlbumTypeComboBox.SelectedIndex = 2;
                _isUpdating = false;
            }
            else
            {
                _albumInfo.Type = AlbumType.AudienceRecording;
                _isUpdating = true;
                AlbumTypeComboBox.SelectedIndex = 1;
                _isUpdating = false;
            }

            UpdateAlbumPreview();
            UpdateAllTrackDisplayTitles();
            UpdateAllTrackInheritedDates();
        }

        private void AlbumTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            _albumInfo.Type = AlbumTypeComboBox.SelectedIndex switch
            {
                1 => AlbumType.AudienceRecording,
                2 => AlbumType.OfficialRelease,
                _ => InferAlbumType()
            };

            UpdateAlbumPreview();
        }

        private AlbumType InferAlbumType()
        {
            if (_albumInfo == null) return AlbumType.AudienceRecording;

            bool hasDate = !string.IsNullOrEmpty(_albumInfo.AlbumDate);
            bool hasVenue = !string.IsNullOrEmpty(_albumInfo.Venue);
            bool hasAlbumName = !string.IsNullOrEmpty(_albumInfo.AlbumName);

            if (hasAlbumName && !hasVenue)
                return AlbumType.OfficialRelease;
            if (hasDate && hasVenue && !hasAlbumName)
                return AlbumType.AudienceRecording;
            if (hasDate && hasVenue && hasAlbumName)
                return AlbumType.OfficialRelease;

            return AlbumType.AudienceRecording;
        }

        private void UpdateAlbumPreview()
        {
            if (_albumInfo == null)
            {
                FolderPreviewTextBlock.Text = "";
                WritePathTextBlock.Text = "";
                return;
            }

            string folderName = _albumInfo.AlbumTitle;
            FolderPreviewTextBlock.Text = folderName;

            if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
            {
                string basePath = _albumInfo.Type == AlbumType.OfficialRelease
                    ? (_librarySettings.OfficialReleasesPath ?? _librarySettings.LibraryRootPath)
                    : _librarySettings.LibraryRootPath;

                WritePathTextBlock.Text = Path.Combine(basePath, folderName);
            }
            else
            {
                WritePathTextBlock.Text = "(Library path not set)";
            }
        }

        // ===== TRACK GRID =====

        private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Playback controls are in the PlayerBar — no action needed here
        }

        private void TracksDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            // No special action needed
        }

        private void TracksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Row.Item is TrackInfoViewModel track)
                {
                    track.Track.IsModified = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        track.UpdateDisplayTitle();
                    }), DispatcherPriority.Background);
                }
            }
        }

        private void TracksDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is TrackInfoViewModel track && track.Track.IsMatched == false)
            {
                e.Row.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD7, 0xBA, 0x7D));
            }
            else
            {
                e.Row.Foreground = new SolidColorBrush(Colors.White);
            }

            // Attach drag-to-reorder handlers
            e.Row.PreviewMouseLeftButtonDown += Row_PreviewMouseLeftButtonDown;
            e.Row.MouseMove += Row_MouseMove;
            e.Row.Drop += Row_Drop;
            e.Row.DragOver += Row_DragOver;
            e.Row.AllowDrop = true;
        }

        private void TracksDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.Header?.ToString() == "Disc")
            {
                e.Handled = true;

                ListSortDirection direction = e.Column.SortDirection != ListSortDirection.Ascending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;

                ICollectionView view = CollectionViewSource.GetDefaultView(TracksDataGrid.ItemsSource);
                view.SortDescriptions.Clear();

                if (direction == ListSortDirection.Ascending)
                {
                    view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
                }
                else
                {
                    view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Descending));
                    view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
                }

                e.Column.SortDirection = direction;
            }
        }

        // ===== CONTEXT MENU =====

        private void TracksDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the DataGridRow under the cursor
            var hit = VisualTreeHelper.HitTest(TracksDataGrid, e.GetPosition(TracksDataGrid));
            if (hit == null) return;

            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not DataGridRow row) return;
            if (row.Item is not TrackInfoViewModel vm) return;

            var clickedTrack = vm.Track;

            // Build dark-themed context menu (same style as AlbumDetailView)
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2)
            };

            var menuItemStyle = new Style(typeof(MenuItem));
            menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0))));
            menuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 6, 20, 6)));
            menuItemStyle.Setters.Add(new Setter(MenuItem.FontSizeProperty, 14.0));

            var playNowItem = new MenuItem { Header = "▶  Play Now", Style = menuItemStyle };
            playNowItem.Click += (s, args) =>
            {
                if (!App.PlaybackService.Playlist.Contains(clickedTrack))
                    App.PlaybackService.Playlist.Add(clickedTrack);
                App.PlaybackService.Play(clickedTrack);
            };
            menu.Items.Add(playNowItem);

            var addItem = new MenuItem { Header = "＋  Add to Playlist", Style = menuItemStyle };
            addItem.Click += (s, args) =>
            {
                if (!App.PlaybackService.Playlist.Contains(clickedTrack))
                    App.PlaybackService.Playlist.Add(clickedTrack);
            };
            menu.Items.Add(addItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        // ===== DRAG-TO-REORDER =====

        private System.Windows.Point _dragStartPoint;
        private TrackInfoViewModel? _draggedItem;
        private bool _isDragging;

        private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedItem = row.Item as TrackInfoViewModel;
                _isDragging = false;
            }
        }

        private void Row_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                System.Windows.Point current = e.GetPosition(null);
                System.Windows.Vector diff = _dragStartPoint - current;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    if (sender is DataGridRow row)
                        DragDrop.DoDragDrop(row, _draggedItem, WpfDragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void Row_DragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)))
            {
                e.Effects = WpfDragDropEffects.Move;

                if (sender is DataGridRow targetRow && _draggedItem != null)
                {
                    var targetItem = targetRow.Item as TrackInfoViewModel;
                    if (targetItem != null && targetItem != _draggedItem)
                        UpdateDropIndicator(targetRow);
                }
            }
            else
            {
                e.Effects = WpfDragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Row_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)) && sender is DataGridRow targetRow)
            {
                var targetItem = targetRow.Item as TrackInfoViewModel;
                if (targetItem != null && _draggedItem != null && targetItem != _draggedItem)
                {
                    int draggedIndex = _tracks.IndexOf(_draggedItem);
                    int targetIndex = _tracks.IndexOf(targetItem);

                    if (draggedIndex >= 0 && targetIndex >= 0)
                    {
                        _tracks.RemoveAt(draggedIndex);
                        _tracks.Insert(targetIndex, _draggedItem);
                        TracksDataGrid.SelectedItem = _draggedItem;
                    }
                }
            }

            HideDropIndicator();
            e.Handled = true;
        }

        private void UpdateDropIndicator(DataGridRow targetRow)
        {
            try
            {
                var position = targetRow.TranslatePoint(new System.Windows.Point(0, 0), TracksDataGrid);
                DropIndicator.Visibility = Visibility.Visible;
                DropIndicator.Margin = new Thickness(5, position.Y, 5, 0);
            }
            catch
            {
                HideDropIndicator();
            }
        }

        private void HideDropIndicator()
        {
            DropIndicator.Visibility = Visibility.Collapsed;
        }

        private void UpdateAllTrackDisplayTitles()
        {
            foreach (var t in _tracks) t.UpdateDisplayTitle();
        }

        private void UpdateAllTrackInheritedDates()
        {
            foreach (var t in _tracks) t.UpdateInheritedDate();
        }

        // ===== ACTION BUTTONS =====

        private async void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Files", "No tracks loaded to normalize.");
                return;
            }

            try
            {
                StatusTextBlock.Text = "Normalizing...";

                var trackList = _tracks.Select(t => t.Track).ToList();
                int matched = _normalizationService.NormalizeAll(trackList);

                TracksDataGrid.Items.Refresh();

                StatusTextBlock.Text = $"Matched {matched} of {_tracks.Count} songs";

                int unmatched = _tracks.Count - matched;
                if (unmatched > 0)
                {
                    var unmatchedTracks = _tracks
                        .Where(t => t.Track.IsMatched == false)
                        .Select(t => t.Track)
                        .ToList();

                    // Find the parent window for dialog ownership
                    var parentWindow = Window.GetWindow(this);
                    var dialog = new UnmatchedSongsDialog(unmatchedTracks, _normalizationService)
                    {
                        Owner = parentWindow
                    };

                    if (dialog.ShowDialog() == true && dialog.ChangesMade)
                    {
                        TracksDataGrid.Items.Refresh();
                        int nowMatched = _tracks.Count(t => t.Track.IsMatched == true);
                        StatusTextBlock.Text = $"Corrections applied. Matched {nowMatched} of {_tracks.Count} songs";
                    }
                    else
                    {
                        await ShowNotificationAsync("Normalization Complete",
                            $"{unmatched} songs not in database (will use original titles). Unmatched songs shown in gold.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error normalizing: {ex.Message}");
                StatusTextBlock.Text = "Normalization error";
            }
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0) return;

            var tracksByDisc = _tracks
                .GroupBy(t => t.Track.DiscNumber > 0 ? t.Track.DiscNumber : 1)
                .OrderBy(g => g.Key);

            foreach (var discGroup in tracksByDisc)
            {
                int discNumber = discGroup.Key;
                int trackIndex = 1;

                foreach (var tw in discGroup)
                {
                    tw.Track.TrackNumber = (discNumber * 100) + trackIndex;
                    tw.Track.IsModified = true;
                    trackIndex++;
                }
            }

            TracksDataGrid.Items.Refresh();
            StatusTextBlock.Text = "Tracks renumbered using disc-aware 101/201/301 convention";
        }

        private async void MusicBrainzButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Tracks", "No tracks loaded. Please select a folder first.");
                return;
            }

            try
            {
                MusicBrainzButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;
                StatusTextBlock.Text = "Looking up album on MusicBrainz...";

                var trackList = _tracks.Select(t => t.Track).ToList();
                var releases = await _musicBrainzService.LookupAllReleasesAsync(trackList);

                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;

                if (releases == null || releases.Count == 0)
                {
                    await ShowNotificationAsync("Not Found",
                        "Could not identify this album using audio fingerprinting.\n\n" +
                        "Possible reasons:\n" +
                        "• fpcalc.exe not configured (check Settings)\n" +
                        "• Album not in MusicBrainz database\n" +
                        "• Audio fingerprints don't match\n\n" +
                        "Please enter the information manually.");
                    StatusTextBlock.Text = "Album lookup failed";
                    return;
                }

                var selector = new ReleaseSelectorDialog("Select MusicBrainz Release", releases);
                if (selector.ShowDialog() == true && selector.SelectedRelease != null)
                {
                    await ApplyMusicBrainzData(selector.SelectedRelease);
                }
                else
                {
                    StatusTextBlock.Text = "Album lookup cancelled";
                }
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;
                await ShowNotificationAsync("Error", $"Error looking up album: {ex.Message}");
                StatusTextBlock.Text = "Album lookup error";
            }
        }

        private async Task ApplyMusicBrainzData(ReleaseOption release)
        {
            try
            {
                StatusTextBlock.Text = "Fetching release tracks...";

                var tracks = await _musicBrainzService.GetReleaseTracksAsync(release.ReleaseId);

                _isUpdating = true;
                ArtistTextBox.Text = release.Artist;
                AlbumNameTextBox.Text = release.Title;
                YearTextBox.Text = release.Year.ToString();
                _isUpdating = false;

                if (_albumInfo != null)
                {
                    _albumInfo.Artist = release.Artist;
                    _albumInfo.AlbumName = release.Title;
                    _albumInfo.Year = release.Year.ToString();
                }

                // Download artwork
                if (!string.IsNullOrEmpty(release.ArtworkUrl))
                {
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient();
                        var artworkData = await httpClient.GetByteArrayAsync(release.ArtworkUrl);
                        if (artworkData != null && artworkData.Length > 0 && _albumInfo != null)
                        {
                            _albumInfo.ArtworkData = artworkData;
                            _albumInfo.ArtworkMimeType = "image/jpeg";
                            UpdateArtworkDisplay();
                        }
                    }
                    catch
                    {
                        // Artwork download failed — not critical
                    }
                }

                // Match track titles by disc + position
                if (tracks != null && tracks.Count > 0)
                {
                    int matchedCount = 0;

                    foreach (var localTrack in _tracks)
                    {
                        var mbTrack = tracks.FirstOrDefault(t =>
                            t.DiscNumber == localTrack.DiscNumber &&
                            t.Position == localTrack.TrackNumber);

                        if (mbTrack != null)
                        {
                            localTrack.Track.SongName = mbTrack.Title;
                            localTrack.Track.IsMatched = true;
                            localTrack.Track.IsModified = true;
                            localTrack.UpdateDisplayTitle();
                            matchedCount++;
                        }
                    }

                    string confidence = (matchedCount == _tracks.Count && _tracks.Count == tracks.Count)
                        ? "High" : "Medium";
                    StatusTextBlock.Text =
                        $"MusicBrainz: Found {release.Title} ({release.Year}) — " +
                        $"{matchedCount}/{_tracks.Count} tracks matched (confidence: {confidence})";
                }
                else
                {
                    StatusTextBlock.Text =
                        $"MusicBrainz: Found {release.Title} ({release.Year}) — No track data";
                }

                TracksDataGrid.Items.Refresh();
                UpdateAlbumPreview();
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error applying MusicBrainz data: {ex.Message}");
                StatusTextBlock.Text = "Error applying MusicBrainz data";
            }
        }

        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                await ShowNotificationAsync("No Files", "No tracks loaded to write.");
                return;
            }

            bool confirmed = await ShowNotificationAsync("Confirm Write",
                $"This will write metadata to {_tracks.Count} audio files. This operation cannot be undone.\n\nContinue?",
                showYesNo: true);

            if (!confirmed) return;

            try
            {
                StatusTextBlock.Text = "Writing metadata...";

                var trackList = _tracks.Select(t => t.Track).ToList();
                _metadataService.WriteMetadata(_albumInfo, trackList);

                _albumInfo.IsModified = false;
                foreach (var t in _tracks) t.Track.IsModified = false;

                StatusTextBlock.Text = $"Successfully wrote metadata to {_tracks.Count} files";
                await ShowNotificationAsync("Success",
                    $"Successfully wrote metadata to {_tracks.Count} files.");
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error writing metadata: {ex.Message}");
                StatusTextBlock.Text = "Error writing metadata";
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                await ShowNotificationAsync("No Files", "No tracks loaded to import.");
                return;
            }

            // Reload settings in case the user updated the library path in Settings
            _librarySettings = LibrarySettings.Load();

            if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
            {
                await ShowNotificationAsync("Library Not Set",
                    "Library path not set. Please configure it in Settings.");
                return;
            }

            // Duplicate check
            bool exists = _libraryImportService.ShowExistsInLibrary(
                _librarySettings.LibraryRootPath,
                _albumInfo,
                _librarySettings.OfficialReleasesPath);

            if (exists)
            {
                bool overwrite = await ShowNotificationAsync("Show Already Exists",
                    $"This album ({_albumInfo.AlbumTitle}) already exists in your library.\n\nOverwrite?",
                    showYesNo: true);

                if (!overwrite) return;
            }

            string destination = WritePathTextBlock.Text;
            bool confirmed = await ShowNotificationAsync("Confirm Import",
                $"Import {_tracks.Count} tracks to library?\n\nDestination:\n{destination}",
                showYesNo: true);

            if (!confirmed) return;

            try
            {
                ImportButton.IsEnabled = false;
                WriteButton.IsEnabled = false;
                StatusTextBlock.Text = "Importing to library...";
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;

                var progress = new Progress<(int current, int total, string status)>(report =>
                {
                    ProgressBar.Value = (report.current * 100.0) / report.total;
                    StatusTextBlock.Text = report.status;
                });

                var trackList = _tracks.Select(t => t.Track).ToList();

                await Task.Run(() =>
                {
                    _libraryImportService.ImportToLibrary(
                        _librarySettings.LibraryRootPath,
                        _albumInfo,
                        trackList,
                        progress,
                        _librarySettings.OfficialReleasesPath);
                });

                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Successfully imported {_tracks.Count} tracks";

                // Remember box set name
                if (_albumInfo.Type == AlbumType.OfficialRelease &&
                    !string.IsNullOrEmpty(_albumInfo.AlbumName))
                {
                    _librarySettings.LastBoxSetName = _albumInfo.AlbumName;
                    _librarySettings.Save();
                }

                await ShowNotificationAsync("Import Complete",
                    $"Successfully imported {_tracks.Count} tracks to library.\n\nDestination:\n{destination}\n\nSwitch to Library to see the new album.");

                // Notify ShellWindow
                ImportCompleted?.Invoke(this, destination);

                // Reset UI for the next import
                ClearView();
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = true;
                WriteButton.IsEnabled = true;
                await ShowNotificationAsync("Import Failed",
                    $"Error importing to library:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
                StatusTextBlock.Text = $"Import failed: {ex.Message}";
            }
        }

        private void ViewInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null || string.IsNullOrEmpty(_albumInfo.InfoFileContent))
                return;

            WpfMessageBox.Show(
                _albumInfo.InfoFileContent,
                _albumInfo.InfoFileName ?? "Info File",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ===== ARTWORK =====

        private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Album Artwork",
                Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var artworkData = File.ReadAllBytes(dlg.FileName);
                    string mimeType = Path.GetExtension(dlg.FileName).ToLower() switch
                    {
                        ".png" => "image/png",
                        _ => "image/jpeg"
                    };

                    _albumInfo.ArtworkData = artworkData;
                    _albumInfo.ArtworkMimeType = mimeType;
                    _albumInfo.IsModified = true;

                    UpdateArtworkDisplay();
                }
                catch (Exception ex)
                {
                    _ = ShowNotificationAsync("Error", $"Error loading artwork: {ex.Message}");
                }
            }
        }

        private void RemoveArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null) return;

            _albumInfo.ArtworkData = null;
            _albumInfo.ArtworkMimeType = null;
            _albumInfo.IsModified = true;

            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
        }

        private void UpdateArtworkDisplay()
        {
            if (_albumInfo?.ArtworkData != null && _albumInfo.ArtworkData.Length > 0)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(_albumInfo.ArtworkData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ArtworkImage.Source = bitmap;
                    ArtworkImage.Visibility = Visibility.Visible;
                    NoArtworkText.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ArtworkImage.Visibility = Visibility.Collapsed;
                    NoArtworkText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ArtworkImage.Visibility = Visibility.Collapsed;
                NoArtworkText.Visibility = Visibility.Visible;
            }
        }

        // ===== NOTIFICATION PANEL =====

        private async Task<bool> ShowNotificationAsync(string title, string message, bool showYesNo = false)
        {
            _notificationResult = new TaskCompletionSource<bool>();

            NotificationTitle.Text = title;
            NotificationMessage.Text = message;

            if (showYesNo)
            {
                NotificationYesButton.Visibility = Visibility.Visible;
                NotificationNoButton.Visibility = Visibility.Visible;
                NotificationOkButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                NotificationYesButton.Visibility = Visibility.Collapsed;
                NotificationNoButton.Visibility = Visibility.Collapsed;
                NotificationOkButton.Visibility = Visibility.Visible;
            }

            NotificationPanel.Visibility = Visibility.Visible;
            return await _notificationResult.Task;
        }

        private void NotificationYes_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(true);
        }

        private void NotificationNo_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(false);
        }

        private void NotificationOk_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(true);
        }

        private void NotificationPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == NotificationPanel)
            {
                NotificationPanel.Visibility = Visibility.Collapsed;
                _notificationResult?.SetResult(false);
            }
        }
    }
}
