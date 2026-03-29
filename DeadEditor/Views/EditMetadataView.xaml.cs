using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeadEditor
{
    public partial class EditMetadataView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibraryShow _show;
        private readonly MetadataService _metadataService;
        private readonly NormalizationService _normalizationService;

        private AlbumInfo? _albumInfo;
        private List<TrackInfo> _tracks = new();
        private bool _isUpdating = false;
        private bool _hasUnsavedChanges = false;

        /// <summary>
        /// The album name for the header bar back button text.
        /// </summary>
        public string AlbumName => _show.Type == AlbumType.OfficialRelease ? _show.AlbumName : _show.Venue;

        /// <summary>
        /// Whether there are unsaved changes (for confirmation dialog on cancel/back).
        /// </summary>
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        /// <summary>
        /// Fired when save completes successfully, so the shell can refresh the album detail view.
        /// </summary>
        public event EventHandler? SaveCompleted;

        public EditMetadataView(ShellWindow shell, LibraryShow show)
        {
            InitializeComponent();
            _shell = shell;
            _show = show;
            _metadataService = new MetadataService();
            _normalizationService = new NormalizationService();
        }

        private void EditMetadataView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        // ===== DATA LOADING =====

        private void LoadData()
        {
            try
            {
                StatusTextBlock.Text = "Reading files...";

                // Read tracks from all folders in edit mode (as-is, no transforms)
                var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };
                _tracks.Clear();

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder))
                        continue;

                    var folderTracks = _metadataService.ReadFolder(folder, editMode: true);
                    _tracks.AddRange(folderTracks);
                }

                if (_tracks.Count == 0)
                {
                    StatusTextBlock.Text = "No audio files found";
                    return;
                }

                // Build AlbumInfo from LibraryShow data (same as MainWindow edit mode)
                _albumInfo = new AlbumInfo
                {
                    FolderPath = _show.FolderPath,
                    Type = _show.Type,
                    AlbumDate = _show.Date ?? "",
                    Venue = _show.Venue ?? "",
                    CityState = !string.IsNullOrEmpty(_show.Location) ? _show.Location :
                               (!string.IsNullOrEmpty(_show.City) && !string.IsNullOrEmpty(_show.State) ? $"{_show.City}, {_show.State}" : ""),
                    AlbumName = _show.AlbumName ?? "",
                    Year = _show.ReleaseYear?.ToString() ?? "",
                    OfficialRelease = _show.OfficialRelease ?? "",
                    Edition = _show.Edition ?? ""
                };

                // Read artist and artwork from first FLAC file
                var firstFile = _tracks.FirstOrDefault()?.FilePath;
                if (firstFile != null)
                {
                    using (var file = TagLib.File.Create(firstFile))
                    {
                        _albumInfo.Artist = file.Tag.FirstPerformer ?? "";

                        var pictures = file.Tag.Pictures;
                        if (pictures.Length > 0)
                        {
                            _albumInfo.ArtworkData = pictures[0].Data.Data;
                            _albumInfo.ArtworkMimeType = pictures[0].MimeType;
                        }
                    }
                }

                // Extract dates from raw titles for display (edit mode doesn't parse them)
                foreach (var track in _tracks)
                {
                    if (string.IsNullOrEmpty(track.TrackDate) && !string.IsNullOrEmpty(track.SongName))
                    {
                        var dateMatch = System.Text.RegularExpressions.Regex.Match(
                            track.SongName, @"\((\d{4}-\d{2}-\d{2})\)");
                        if (dateMatch.Success)
                        {
                            track.TrackDate = dateMatch.Groups[1].Value;
                        }
                    }
                }

                // Sort tracks: multi-night by date then track#, else by disc then track#
                var distinctDates = _tracks.Where(t => !string.IsNullOrEmpty(t.TrackDate))
                                          .Select(t => t.TrackDate)
                                          .Distinct()
                                          .Count();

                var hasDiscNumbers = _tracks.Any(t => t.DiscNumber > 1);

                if (distinctDates > 1)
                {
                    _tracks = _tracks.OrderBy(t => string.IsNullOrEmpty(t.TrackDate) ? "9999-99-99" : t.TrackDate)
                                     .ThenBy(t => t.DiscNumber)
                                     .ThenBy(t => t.TrackNumber)
                                     .ToList();
                }
                else if (hasDiscNumbers)
                {
                    _tracks = _tracks.OrderBy(t => t.DiscNumber)
                                     .ThenBy(t => t.TrackNumber)
                                     .ToList();
                }
                else
                {
                    _tracks = _tracks.OrderBy(t => t.TrackNumber).ToList();
                }

                // Subscribe to property changes on each track for change tracking
                foreach (var track in _tracks)
                {
                    track.PropertyChanged += Track_PropertyChanged;
                }

                // Bind tracks to DataGrid
                TracksDataGrid.ItemsSource = _tracks;

                // Populate album info fields
                RefreshUI();

                StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";
                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading: {ex.Message}";
            }
        }

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

                // Album type display
                AlbumTypeText.Text = _albumInfo.Type == AlbumType.OfficialRelease ? "Official Release" : "Audience Recording";

                // Artwork
                LoadArtwork();
            }

            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";

            _isUpdating = false;
        }

        private void LoadArtwork()
        {
            if (_albumInfo?.ArtworkData != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new System.IO.MemoryStream(_albumInfo.ArtworkData);
                    bitmap.EndInit();

                    AlbumArtImage.Source = bitmap;
                    AlbumArtImage.Visibility = Visibility.Visible;
                    ArtworkPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
                catch { }
            }

            // Try file-based artwork
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;

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

                        AlbumArtImage.Source = bitmap;
                        AlbumArtImage.Visibility = Visibility.Visible;
                        ArtworkPlaceholder.Visibility = Visibility.Collapsed;
                        return;
                    }
                    catch { }
                }
            }

            // No artwork found
            AlbumArtImage.Source = null;
            AlbumArtImage.Visibility = Visibility.Collapsed;
            ArtworkPlaceholder.Visibility = Visibility.Visible;
        }

        // ===== ALBUM INFO CHANGE TRACKING =====

        private void AlbumInfo_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            // Sync text fields back to AlbumInfo
            _albumInfo.Artist = ArtistTextBox.Text;
            _albumInfo.AlbumDate = AlbumDateTextBox.Text;
            _albumInfo.Venue = VenueTextBox.Text;
            _albumInfo.CityState = CityStateTextBox.Text;
            _albumInfo.AlbumName = AlbumNameTextBox.Text;
            _albumInfo.Year = YearTextBox.Text;
            _albumInfo.IsModified = true;

            _hasUnsavedChanges = true;
        }

        // ===== SAVE / CANCEL =====

        /// <summary>
        /// Save changes: writes metadata to FLAC files, then navigates back.
        /// Called from HeaderBar "Save Changes" button.
        /// </summary>
        public async Task SaveChangesAsync()
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                StatusTextBlock.Text = "No tracks to save";
                return;
            }

            try
            {
                StatusTextBlock.Text = "Writing metadata to files...";
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                // Write metadata to FLAC files IN-PLACE (no copy, no new folder)
                await Task.Run(() =>
                {
                    _metadataService.WriteMetadata(_albumInfo, _tracks);
                });

                // Update the LibraryShow object in-place so the library grid
                // reflects the edited fields without creating a duplicate entry.
                // The folder path doesn't change — only the cached display fields do.
                _show.AlbumName = _albumInfo.AlbumName ?? "";
                _show.Venue = _albumInfo.Venue ?? "";
                _show.Date = _albumInfo.AlbumDate ?? "";
                _show.Edition = _albumInfo.Edition ?? "";
                _show.OfficialRelease = _albumInfo.OfficialRelease ?? "";

                if (!string.IsNullOrEmpty(_albumInfo.CityState))
                {
                    var parts = _albumInfo.CityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                    _show.City = parts.Length > 0 ? parts[0] : "";
                    _show.State = parts.Length > 1 ? parts[1] : "";
                    _show.Location = _albumInfo.CityState;
                }

                if (int.TryParse(_albumInfo.Year, out var year))
                {
                    _show.ReleaseYear = year;
                }

                // Re-cache track titles for search
                _show.TrackTitles = _tracks
                    .Where(t => !string.IsNullOrEmpty(t.SongName))
                    .Select(t => t.SongName!)
                    .ToList();

                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Saved {_tracks.Count} files";
                _hasUnsavedChanges = false;

                // Notify that save completed (so library can refresh)
                SaveCompleted?.Invoke(this, EventArgs.Empty);

                // Navigate back to album detail
                _shell.Navigation.GoBack();
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Save failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Error saving changes:\n\n{ex.Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Cancel: discard changes and navigate back.
        /// Called from HeaderBar "Cancel" button.
        /// </summary>
        public void CancelEdit()
        {
            if (_hasUnsavedChanges)
            {
                var result = System.Windows.MessageBox.Show(
                    "You have unsaved changes. Discard them?",
                    "Discard Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
            }

            _shell.Navigation.GoBack();
        }

        /// <summary>
        /// Navigate back (with unsaved changes confirmation).
        /// Called from HeaderBar back button.
        /// </summary>
        public void NavigateBack()
        {
            CancelEdit();
        }

        // ===== TRACK GRID EDITING =====

        private void Track_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Track any property change (Segue checkbox, etc.) as unsaved
            if (_isUpdating) return;

            if (e.PropertyName == nameof(TrackInfo.Segue) ||
                e.PropertyName == nameof(TrackInfo.SongName) ||
                e.PropertyName == nameof(TrackInfo.TrackDate) ||
                e.PropertyName == nameof(TrackInfo.DiscNumber))
            {
                _hasUnsavedChanges = true;
                if (sender is TrackInfo track)
                {
                    track.IsModified = true;
                }
            }
        }

        private void TracksDataGrid_BeginningEdit(object sender, System.Windows.Controls.DataGridBeginningEditEventArgs e)
        {
            // Nothing to block — editable columns are already controlled by IsReadOnly per-column
        }

        private void TracksDataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit)
            {
                _hasUnsavedChanges = true;

                // Mark the individual track as modified
                if (e.Row.Item is TrackInfo track)
                {
                    track.IsModified = true;
                }
            }
        }

        // ===== NORMALIZE / RENUMBER =====

        private void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                StatusTextBlock.Text = "No tracks to normalize";
                return;
            }

            try
            {
                StatusTextBlock.Text = "Normalizing...";

                // Clean SongName before normalizing — edit mode loads raw FLAC titles
                // (e.g., "Dark Star > > (1968-02-23)") which need the same ParseTitleAndDate
                // cleanup that import mode gets during ReadFolder.
                foreach (var track in _tracks)
                {
                    // Preserve raw title for reconstruction after normalization
                    if (string.IsNullOrEmpty(track.RawTitle))
                        track.RawTitle = track.SongName;

                    var (cleanName, date) = _metadataService.ParseTitleAndDate(track.SongName);
                    track.SongName = cleanName;
                    if (!string.IsNullOrEmpty(date) && string.IsNullOrEmpty(track.TrackDate))
                        track.TrackDate = date;
                }

                int matched = _normalizationService.NormalizeAll(_tracks);

                TracksDataGrid.Items.Refresh();
                _hasUnsavedChanges = true;

                int unmatched = _tracks.Count - matched;
                StatusTextBlock.Text = $"Matched {matched} of {_tracks.Count} songs" +
                    (unmatched > 0 ? $" ({unmatched} unmatched)" : "");

                if (unmatched > 0)
                {
                    var unmatchedTracks = _tracks
                        .Where(t => t.IsMatched == false)
                        .ToList();

                    var parentWindow = Window.GetWindow(this);
                    var dialog = new UnmatchedSongsDialog(unmatchedTracks, _normalizationService)
                    {
                        Owner = parentWindow
                    };

                    if (dialog.ShowDialog() == true && dialog.ChangesMade)
                    {
                        TracksDataGrid.Items.Refresh();
                        int nowMatched = _tracks.Count(t => t.IsMatched == true);
                        StatusTextBlock.Text = $"Corrections applied. Matched {nowMatched} of {_tracks.Count} songs";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Normalization error: {ex.Message}";
            }
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0) return;

            var tracksByDisc = _tracks
                .GroupBy(t => t.DiscNumber > 0 ? t.DiscNumber : 1)
                .OrderBy(g => g.Key);

            foreach (var discGroup in tracksByDisc)
            {
                int discNumber = discGroup.Key;
                int trackIndex = 1;

                foreach (var track in discGroup)
                {
                    track.TrackNumber = (discNumber * 100) + trackIndex;
                    track.IsModified = true;
                    trackIndex++;
                }
            }

            TracksDataGrid.Items.Refresh();
            _hasUnsavedChanges = true;
            StatusTextBlock.Text = "Tracks renumbered using disc-aware 101/201/301 convention";
        }
    }
}
