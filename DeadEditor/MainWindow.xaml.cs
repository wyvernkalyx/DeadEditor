using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeadEditor;

/// <summary>
/// MainWindow - Concert Import & Metadata Editor (Redesigned)
///
/// Single-column editable track list with inline editing.
/// Album info bar with unified fields for all types.
/// Date auto-append when track date differs from album date.
/// MusicBrainz as simple populate action (no confirmation dialog).
/// </summary>
public partial class MainWindow : Window
{
    // Services
    private readonly MetadataService _metadataService;
    private readonly NormalizationService _normalizationService;
    private readonly LibraryImportService _libraryImportService;
    private readonly MusicBrainzService _musicBrainzService;
    private LibrarySettings _librarySettings;

    // Data
    private ObservableCollection<TrackInfoViewModel> _tracks = new();
    private AlbumInfo? _albumInfo;
    private bool _isUpdating = false;
    private TaskCompletionSource<bool>? _notificationResult;

    // Audio playback (using singleton)
    private AudioPlayerService _audioPlayer => App.PlaybackService;
    private readonly DispatcherTimer _playbackTimer;
    private int _currentTrackIndex = -1;
    private bool _isScrubbing = false;

    public MainWindow()
    {
        InitializeComponent();

        _metadataService = new MetadataService();
        _normalizationService = new NormalizationService();
        _libraryImportService = new LibraryImportService(_metadataService);
        _librarySettings = LibrarySettings.Load();
        _musicBrainzService = new MusicBrainzService("asa4wLQhwJ", _librarySettings);

        // Subscribe to singleton audio player events
        _audioPlayer.PlaybackStopped += AudioPlayer_PlaybackStopped;

        // Initialize playback timer
        _playbackTimer = new DispatcherTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(100);
        _playbackTimer.Tick += PlaybackTimer_Tick;

        // Restore window position
        RestoreWindowPosition();

        // Setup DataGrid
        TracksDataGrid.ItemsSource = _tracks;
    }

    private void RestoreWindowPosition()
    {
        if (_librarySettings.MainWindowLeft.HasValue && _librarySettings.MainWindowTop.HasValue)
        {
            Left = _librarySettings.MainWindowLeft.Value;
            Top = _librarySettings.MainWindowTop.Value;
        }

        if (_librarySettings.MainWindowWidth.HasValue && _librarySettings.MainWindowHeight.HasValue)
        {
            Width = _librarySettings.MainWindowWidth.Value;
            Height = _librarySettings.MainWindowHeight.Value;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Stop playback (don't dispose singleton, App.xaml.cs handles that)
        StopPlaybackAndCleanup();

        // Unsubscribe from events to prevent memory leaks
        _audioPlayer.PlaybackStopped -= AudioPlayer_PlaybackStopped;

        // Save window position
        _librarySettings.MainWindowLeft = Left;
        _librarySettings.MainWindowTop = Top;
        _librarySettings.MainWindowWidth = Width;
        _librarySettings.MainWindowHeight = Height;
        _librarySettings.Save();
    }

    // ===== FOLDER LOADING =====

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder containing audio files (FLAC/MP3)"
        };

        if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathTextBox.Text = folderDialog.SelectedPath;
            LoadFolder(folderDialog.SelectedPath);
        }
    }

    public async void LoadFolder(string folderPath)
    {
        try
        {
            // Stop any ongoing playback
            StopPlaybackAndCleanup();

            FolderPathTextBox.Text = folderPath;
            StatusTextBlock.Text = "Reading files...";

            // Read tracks
            var trackList = _metadataService.ReadFolder(folderPath)
                .OrderBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .ToList();

            if (trackList.Count == 0)
            {
                await ShowNotificationAsync("No Files", "No audio files (FLAC/MP3) found in the selected folder.");
                StatusTextBlock.Text = "No audio files found";
                return;
            }

            // Read album info
            _albumInfo = _metadataService.ReadAlbumInfo(folderPath, trackList);

            // Convert to ViewModels
            _tracks.Clear();
            foreach (var track in trackList)
            {
                _tracks.Add(new TrackInfoViewModel(track, _albumInfo));
            }

            // Update UI
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
        // Re-read current folder
        if (!string.IsNullOrEmpty(FolderPathTextBox.Text) && Directory.Exists(FolderPathTextBox.Text))
        {
            LoadFolder(FolderPathTextBox.Text);
        }
    }

    private void RefreshUI()
    {
        _isUpdating = true;

        if (_albumInfo != null)
        {
            // Populate album info fields
            ArtistTextBox.Text = _albumInfo.Artist ?? "";
            AlbumDateTextBox.Text = _albumInfo.AlbumDate ?? "";
            VenueTextBox.Text = _albumInfo.Venue ?? "";
            CityStateTextBox.Text = _albumInfo.CityState ?? "";
            AlbumNameTextBox.Text = _albumInfo.AlbumName ?? "";
            YearTextBox.Text = _albumInfo.Year ?? "";

            // Set album type dropdown (simplified: Auto-detect, Audience Recording, Official Release)
            AlbumTypeComboBox.SelectedIndex = _albumInfo.Type switch
            {
                AlbumType.AudienceRecording => 1,
                AlbumType.OfficialRelease => 2,
                _ => 0 // Auto-detect
            };

            // Update artwork
            UpdateArtworkDisplay();

            // Update info button
            ViewInfoButton.IsEnabled = !string.IsNullOrEmpty(_albumInfo.InfoFileContent);
        }

        // Refresh previews
        UpdateAlbumPreview();
        UpdateAllTrackDisplayTitles();

        _isUpdating = false;
    }

    private void ClearView()
    {
        _isUpdating = true;

        _tracks.Clear();
        _albumInfo = null;
        FolderPathTextBox.Text = "";
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

        // Re-enable buttons
        ImportButton.IsEnabled = true;
        WriteButton.IsEnabled = true;

        // Clear artwork
        ArtworkImage.Visibility = Visibility.Collapsed;
        NoArtworkText.Visibility = Visibility.Visible;

        StatusTextBlock.Text = "Ready";

        _isUpdating = false;
    }

    // ===== ALBUM INFO BAR CHANGES =====

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

        // Auto-detect Album Type based on Album/Release Name field
        // If Album Name has text → Official Release
        // If Album Name is empty → Audience Recording
        if (!string.IsNullOrWhiteSpace(_albumInfo.AlbumName))
        {
            // Album Name populated → Official Release
            _albumInfo.Type = AlbumType.OfficialRelease;
            _isUpdating = true;
            AlbumTypeComboBox.SelectedIndex = 2; // Official Release
            _isUpdating = false;
        }
        else
        {
            // Album Name empty → Audience Recording
            _albumInfo.Type = AlbumType.AudienceRecording;
            _isUpdating = true;
            AlbumTypeComboBox.SelectedIndex = 1; // Audience Recording
            _isUpdating = false;
        }

        UpdateAlbumPreview();
        UpdateAllTrackDisplayTitles(); // Date changes affect track titles
        UpdateAllTrackInheritedDates(); // Update inherited dates when album date changes
    }

    private void AlbumTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || _albumInfo == null) return;

        _albumInfo.Type = AlbumTypeComboBox.SelectedIndex switch
        {
            1 => AlbumType.AudienceRecording,
            2 => AlbumType.OfficialRelease,
            _ => InferAlbumType() // Auto-detect
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

        return AlbumType.AudienceRecording; // Default
    }

    private void UpdateAlbumPreview()
    {
        if (_albumInfo == null)
        {
            FolderPreviewTextBlock.Text = "";
            WritePathTextBlock.Text = "";
            return;
        }

        // Update folder name preview
        string folderName = _albumInfo.AlbumTitle;
        FolderPreviewTextBlock.Text = folderName;

        // Update write path
        if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
        {
            string basePath = _albumInfo.Type == AlbumType.OfficialRelease
                ? (_librarySettings.OfficialReleasesPath ?? _librarySettings.LibraryRootPath)
                : _librarySettings.LibraryRootPath;

            string fullPath = Path.Combine(basePath, folderName);
            WritePathTextBlock.Text = fullPath;
        }
        else
        {
            WritePathTextBlock.Text = "(Library path not set)";
        }
    }

    // ===== TRACK LIST - INLINE EDITING =====

    private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Enable play button if track selected
        if (TracksDataGrid.SelectedItem != null)
        {
            PlayPauseButton.IsEnabled = true;
        }
    }

    private void TracksDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        // Called when entering edit mode - no special action needed
    }

    private void TracksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            var track = e.Row.Item as TrackInfoViewModel;
            if (track != null)
            {
                // Mark as modified
                track.Track.IsModified = true;

                // Schedule display title update (after DataGrid commits the edit)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    track.UpdateDisplayTitle();
                }), DispatcherPriority.Background);
            }
        }
    }

    private void TracksDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        var track = e.Row.Item as TrackInfoViewModel;
        if (track != null && track.Track.IsMatched == false)
        {
            // Highlight unmatched songs in gold (only when explicitly false, not null)
            e.Row.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD7, 0xBA, 0x7D));
        }
        else
        {
            e.Row.Foreground = new SolidColorBrush(Colors.White);
        }

        // Attach drag-to-reorder handlers to each row
        e.Row.PreviewMouseLeftButtonDown += Row_PreviewMouseLeftButtonDown;
        e.Row.MouseMove += Row_MouseMove;
        e.Row.Drop += Row_Drop;
        e.Row.DragOver += Row_DragOver;
        e.Row.AllowDrop = true;
    }

    private void TracksDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Check if sorting by Disc column
        if (e.Column.Header.ToString() == "Disc")
        {
            // Cancel default sorting
            e.Handled = true;

            // Determine sort direction
            ListSortDirection direction = e.Column.SortDirection != ListSortDirection.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            // Apply compound sort: Disc (primary), then Track Number (secondary)
            ICollectionView view = CollectionViewSource.GetDefaultView(TracksDataGrid.ItemsSource);
            view.SortDescriptions.Clear();

            if (direction == ListSortDirection.Ascending)
            {
                // Ascending: Disc 1 Track 1...N, Disc 2 Track 1...N, Disc 3 Track 1...N
                view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
            }
            else
            {
                // Descending: Disc 3 Track 1...N, Disc 2 Track 1...N, Disc 1 Track 1...N
                view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
            }

            // Update column sort direction indicator
            e.Column.SortDirection = direction;
        }
        // For all other columns, use default sorting (e.Handled remains false)
    }

    // ===== DRAG-TO-REORDER FUNCTIONALITY =====

    private System.Windows.Point _dragStartPoint;
    private TrackInfoViewModel? _draggedItem;
    private bool _isDragging;

    private void Row_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedItem = row.Item as TrackInfoViewModel;
            _isDragging = false;
        }
    }

    private void Row_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
        {
            System.Windows.Point currentPosition = e.GetPosition(null);
            System.Windows.Vector diff = _dragStartPoint - currentPosition;

            // Only start drag if mouse moved enough (prevents accidental drags during cell edits)
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                var row = sender as DataGridRow;
                if (row != null)
                {
                    System.Windows.DragDrop.DoDragDrop(row, _draggedItem, System.Windows.DragDropEffects.Move);
                }
                _isDragging = false;
            }
        }
    }

    private void Row_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.Move;

            // Show visual drop indicator
            if (sender is DataGridRow targetRow && _draggedItem != null)
            {
                var targetItem = targetRow.Item as TrackInfoViewModel;
                if (targetItem != null && targetItem != _draggedItem)
                {
                    // Update drop indicator position (will be implemented in XAML)
                    UpdateDropIndicator(targetRow);
                }
            }
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Row_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)) && sender is DataGridRow targetRow)
        {
            var targetItem = targetRow.Item as TrackInfoViewModel;
            if (targetItem != null && _draggedItem != null && targetItem != _draggedItem)
            {
                // Get current indices
                int draggedIndex = _tracks.IndexOf(_draggedItem);
                int targetIndex = _tracks.IndexOf(targetItem);

                if (draggedIndex >= 0 && targetIndex >= 0)
                {
                    // Remove from old position
                    _tracks.RemoveAt(draggedIndex);

                    // Insert at new position
                    if (draggedIndex < targetIndex)
                    {
                        // If dragging down, adjust index because we removed item above
                        _tracks.Insert(targetIndex, _draggedItem);
                    }
                    else
                    {
                        // If dragging up, insert at target position
                        _tracks.Insert(targetIndex, _draggedItem);
                    }

                    // Clear selection and reselect moved row
                    TracksDataGrid.SelectedItem = _draggedItem;
                }
            }
        }

        // Hide drop indicator
        HideDropIndicator();
        e.Handled = true;
    }

    private void UpdateDropIndicator(DataGridRow targetRow)
    {
        try
        {
            // Get the position of the target row relative to the DataGrid
            var position = targetRow.TranslatePoint(new System.Windows.Point(0, 0), TracksDataGrid);

            // Show the indicator at the top of the target row
            DropIndicator.Visibility = Visibility.Visible;
            DropIndicator.Margin = new Thickness(5, position.Y, 5, 0);
        }
        catch
        {
            // If positioning fails, hide the indicator
            HideDropIndicator();
        }
    }

    private void HideDropIndicator()
    {
        DropIndicator.Visibility = Visibility.Collapsed;
    }

    private void UpdateAllTrackDisplayTitles()
    {
        foreach (var track in _tracks)
        {
            track.UpdateDisplayTitle();
        }
    }

    private void UpdateAllTrackInheritedDates()
    {
        foreach (var track in _tracks)
        {
            track.UpdateInheritedDate();
        }
    }

    // ===== ACTIONS =====

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

            // Extract TrackInfo objects
            var trackList = _tracks.Select(t => t.Track).ToList();

            // Normalize (sets IsMatched flag on each track)
            int matched = _normalizationService.NormalizeAll(trackList);

            // Refresh grid to update colors based on IsMatched
            TracksDataGrid.Items.Refresh();

            StatusTextBlock.Text = $"Matched {matched} of {_tracks.Count} songs";

            int unmatched = _tracks.Count - matched;
            if (unmatched > 0)
            {
                // Get list of unmatched tracks
                var unmatchedTracks = _tracks
                    .Where(t => t.Track.IsMatched == false)
                    .Select(t => t.Track)
                    .ToList();

                // Show UnmatchedSongsDialog to let user correct them
                var dialog = new UnmatchedSongsDialog(unmatchedTracks, _normalizationService)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true && dialog.ChangesMade)
                {
                    // User applied corrections - refresh the grid
                    TracksDataGrid.Items.Refresh();
                    StatusTextBlock.Text = $"Corrections applied. Matched {matched + unmatchedTracks.Count(t => t.IsMatched == true)} of {_tracks.Count} songs";
                }
                else
                {
                    // User skipped - show the original notification
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

        // Group tracks by disc number (treat missing/zero as Disc 1)
        var tracksByDisc = _tracks
            .GroupBy(t => t.Track.DiscNumber > 0 ? t.Track.DiscNumber : 1)
            .OrderBy(g => g.Key);

        // Renumber tracks using 101/201/301 convention
        foreach (var discGroup in tracksByDisc)
        {
            int discNumber = discGroup.Key;
            int trackIndex = 1;

            foreach (var trackWrapper in discGroup)
            {
                // Calculate track number: Disc 1 → 101, 102, 103; Disc 2 → 201, 202, 203
                trackWrapper.Track.TrackNumber = (discNumber * 100) + trackIndex;
                trackWrapper.Track.IsModified = true;
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

            // Show release selector
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

            // Fetch track data
            var tracks = await _musicBrainzService.GetReleaseTracksAsync(release.ReleaseId);

            // Update album fields
            _isUpdating = true;
            ArtistTextBox.Text = release.Artist;
            AlbumNameTextBox.Text = release.Title;
            YearTextBox.Text = release.Year.ToString();
            _isUpdating = false;

            _albumInfo.Artist = release.Artist;
            _albumInfo.AlbumName = release.Title;
            _albumInfo.Year = release.Year.ToString();

            // Download artwork if available
            if (!string.IsNullOrEmpty(release.ArtworkUrl))
            {
                try
                {
                    using (var httpClient = new System.Net.Http.HttpClient())
                    {
                        var artworkData = await httpClient.GetByteArrayAsync(release.ArtworkUrl);
                        if (artworkData != null && artworkData.Length > 0)
                        {
                            _albumInfo.ArtworkData = artworkData;
                            _albumInfo.ArtworkMimeType = "image/jpeg";
                            UpdateArtworkDisplay();
                        }
                    }
                }
                catch
                {
                    // Artwork download failed - not critical
                }
            }

            // Update track titles from MusicBrainz
            if (tracks != null && tracks.Count > 0)
            {
                for (int i = 0; i < Math.Min(_tracks.Count, tracks.Count); i++)
                {
                    _tracks[i].Track.SongName = tracks[i].Title;
                    _tracks[i].Track.IsMatched = true;
                    _tracks[i].Track.IsModified = true;
                    _tracks[i].UpdateDisplayTitle();
                }
            }

            TracksDataGrid.Items.Refresh();
            UpdateAlbumPreview();

            StatusTextBlock.Text = $"MusicBrainz: Found {release.Title} ({release.Year})";
        }
        catch (Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error applying MusicBrainz data: {ex.Message}");
            StatusTextBlock.Text = "Error applying MusicBrainz data";
        }
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("=== WriteButton_Click START ===");

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
            foreach (var track in _tracks)
            {
                track.Track.IsModified = false;
            }

            StatusTextBlock.Text = $"Successfully wrote metadata to {_tracks.Count} files";
            await ShowNotificationAsync("Success", $"Successfully wrote metadata to {_tracks.Count} files.");
        }
        catch (Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error writing metadata: {ex.Message}");
            StatusTextBlock.Text = "Error writing metadata";
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("=== ImportButton_Click START ===");
        System.Diagnostics.Debug.WriteLine($"Tracks count: {_tracks.Count}");
        System.Diagnostics.Debug.WriteLine($"AlbumInfo null: {_albumInfo == null}");
        System.Diagnostics.Debug.WriteLine($"LibraryRootPath: {_librarySettings.LibraryRootPath}");

        if (_tracks.Count == 0 || _albumInfo == null)
        {
            System.Diagnostics.Debug.WriteLine("Showing 'No Files' dialog");
            await ShowNotificationAsync("No Files", "No tracks loaded to import.");
            return;
        }

        if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
        {
            System.Diagnostics.Debug.WriteLine("Showing 'Library Not Set' dialog");
            await ShowNotificationAsync("Library Not Set",
                "Library path not set. Please configure it in Settings (from Library window).");
            return;
        }

        System.Diagnostics.Debug.WriteLine("Passed validation, checking for duplicates...");

        // Check for duplicates
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

        // Show confirmation with destination
        string destination = WritePathTextBlock.Text;
        bool confirmed = await ShowNotificationAsync("Confirm Import",
            $"Import {_tracks.Count} tracks to library?\n\nDestination:\n{destination}",
            showYesNo: true);

        if (!confirmed) return;

        try
        {
            // Disable buttons and show immediate feedback
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

            await ShowNotificationAsync("Import Complete",
                $"Successfully imported {_tracks.Count} tracks to library.\n\nDestination:\n{destination}");

            // Remember box set name if applicable
            if (_albumInfo.Type == AlbumType.OfficialRelease && !string.IsNullOrEmpty(_albumInfo.AlbumName))
            {
                _librarySettings.LastBoxSetName = _albumInfo.AlbumName;
                _librarySettings.Save();
            }

            // Clear view (this will reset buttons)
            ClearView();
        }
        catch (Exception ex)
        {
            ProgressBar.Visibility = Visibility.Collapsed;
            ImportButton.IsEnabled = true;
            WriteButton.IsEnabled = true;
            await ShowNotificationAsync("Import Failed", $"Error importing to library:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
            StatusTextBlock.Text = $"Import failed: {ex.Message}";
        }
    }

    private void ViewInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null || string.IsNullOrEmpty(_albumInfo.InfoFileContent))
            return;

        // TODO: Create InfoFileViewer window
        System.Windows.MessageBox.Show(_albumInfo.InfoFileContent, _albumInfo.InfoFileName ?? "Info File",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ===== ARTWORK =====

    private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Album Artwork",
            Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var artworkData = File.ReadAllBytes(dialog.FileName);
                string mimeType = Path.GetExtension(dialog.FileName).ToLower() switch
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
                ShowNotificationAsync("Error", $"Error loading artwork: {ex.Message}");
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

    // ===== AUDIO PLAYBACK =====

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            PlayPauseButton.Content = "▶";
            _playbackTimer.Stop();
        }
        else if (_currentTrackIndex >= 0)
        {
            _audioPlayer.Play();
            PlayPauseButton.Content = "⏸";
            _playbackTimer.Start();
        }
        else if (TracksDataGrid.SelectedIndex >= 0)
        {
            PlayTrack(TracksDataGrid.SelectedIndex);
        }
    }

    private void PreviousTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrackIndex > 0)
        {
            PlayTrack(_currentTrackIndex - 1);
        }
    }

    private void NextTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrackIndex < _tracks.Count - 1)
        {
            PlayTrack(_currentTrackIndex + 1);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopPlaybackAndCleanup();
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;

        try
        {
            _currentTrackIndex = index;
            var track = _tracks[index].Track;

            _audioPlayer.LoadFile(track.FilePath);
            _audioPlayer.Play();

            PlayPauseButton.Content = "⏸";
            PlayPauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            PreviousTrackButton.IsEnabled = index > 0;
            NextTrackButton.IsEnabled = index < _tracks.Count - 1;

            NowPlayingText.Text = $"♪ {track.SongName ?? track.Title}";
            TotalTimeText.Text = FormatTime(_audioPlayer.TotalDuration);
            ProgressSlider.IsEnabled = true;

            _playbackTimer.Start();
        }
        catch (Exception ex)
        {
            ShowNotificationAsync("Playback Error", $"Error playing track: {ex.Message}");
        }
    }

    private void StopPlaybackAndCleanup()
    {
        _playbackTimer.Stop();
        _audioPlayer.Stop();
        _currentTrackIndex = -1;

        PlayPauseButton.Content = "▶";
        PlayPauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        PreviousTrackButton.IsEnabled = false;
        NextTrackButton.IsEnabled = false;
        ProgressSlider.IsEnabled = false;

        NowPlayingText.Text = "No track playing";
        CurrentTimeText.Text = "0:00";
        TotalTimeText.Text = "0:00";
        ProgressSlider.Value = 0;
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioPlayer.IsPlaying && !_isScrubbing)
        {
            CurrentTimeText.Text = FormatTime(_audioPlayer.CurrentPosition);
            ProgressSlider.Value = (_audioPlayer.CurrentPosition.TotalSeconds / _audioPlayer.TotalDuration.TotalSeconds) * 100;
        }
    }

    private void AudioPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Auto-advance to next track if not at end
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _tracks.Count - 1)
            {
                PlayTrack(_currentTrackIndex + 1);
            }
            else
            {
                StopPlaybackAndCleanup();
            }
        });
    }

    private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isScrubbing = true;
    }

    private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isScrubbing = false;
        var position = TimeSpan.FromSeconds((_audioPlayer.TotalDuration.TotalSeconds * ProgressSlider.Value) / 100);
        _audioPlayer.Seek(position);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioPlayer != null)
        {
            _audioPlayer.Volume = (float)(VolumeSlider.Value / 100.0);
        }
    }

    private string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
    }

    // ===== NOTIFICATION PANEL =====

    private async Task<bool> ShowNotificationAsync(string title, string message, bool showYesNo = false)
    {
        System.Diagnostics.Debug.WriteLine($"ShowNotificationAsync called: {title} - {message}");
        _notificationResult = new TaskCompletionSource<bool>();

        NotificationTitle.Text = title;
        NotificationMessage.Text = message;
        System.Diagnostics.Debug.WriteLine("Set notification text, configuring buttons...");

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
        System.Diagnostics.Debug.WriteLine("NotificationPanel visibility set to Visible, awaiting user response...");

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
        // Close on click outside dialog
        if (e.OriginalSource == NotificationPanel)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(false);
        }
    }
}

/// <summary>
/// ViewModel for TrackInfo that handles display title computation and property change notification
/// </summary>
public class TrackInfoViewModel : INotifyPropertyChanged
{
    public TrackInfo Track { get; }
    private readonly AlbumInfo _albumInfo;
    private string _displayTitle;
    private string _inheritedDate;

    public TrackInfoViewModel(TrackInfo track, AlbumInfo albumInfo)
    {
        Track = track;
        _albumInfo = albumInfo;
        _displayTitle = "";
        _inheritedDate = "";
        UpdateDisplayTitle();
        UpdateInheritedDate();

        // Subscribe to TrackInfo property changes to update DisplayTitle when SongName changes
        Track.PropertyChanged += Track_PropertyChanged;
    }

    private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackInfo.SongName))
        {
            UpdateDisplayTitle();
        }
    }

    public string DisplayTitle
    {
        get => _displayTitle;
        set
        {
            if (_displayTitle != value)
            {
                _displayTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string InheritedDate
    {
        get => _inheritedDate;
        set
        {
            if (_inheritedDate != value)
            {
                _inheritedDate = value;
                OnPropertyChanged();
            }
        }
    }

    // Proxy properties for DataGrid binding
    public int TrackNumber
    {
        get => Track.TrackNumber;
        set
        {
            Track.TrackNumber = value;
            OnPropertyChanged();
        }
    }

    public int DiscNumber
    {
        get => Track.DiscNumber;
        set
        {
            Track.DiscNumber = value;
            Track.IsModified = true;
            OnPropertyChanged();
        }
    }

    public string SongName
    {
        get => Track.SongName ?? "";
        set
        {
            Track.SongName = value;
            OnPropertyChanged();
            UpdateDisplayTitle();
        }
    }

    public bool Segue
    {
        get => Track.Segue;
        set
        {
            Track.Segue = value;
            OnPropertyChanged();
            UpdateDisplayTitle(); // Update title when segue checkbox changes
        }
    }

    public string TrackDate
    {
        get => Track.TrackDate ?? "";
        set
        {
            Track.TrackDate = string.IsNullOrWhiteSpace(value) ? "" : value;
            OnPropertyChanged();
            UpdateDisplayTitle();
        }
    }

    public string Duration => Track.Duration;

    public void UpdateDisplayTitle()
    {
        var effectiveDate = string.IsNullOrEmpty(Track.TrackDate) ? _albumInfo?.AlbumDate : Track.TrackDate;
        var songName = Track.SongName ?? Track.Title ?? "";

        // Append segue marker if segue is checked
        if (Track.Segue)
        {
            songName += " >";
        }

        // ALWAYS append date to title (even if it matches album date)
        if (!string.IsNullOrEmpty(effectiveDate))
        {
            DisplayTitle = $"{songName} ({effectiveDate})";
        }
        else
        {
            DisplayTitle = songName;
        }
    }

    public void UpdateInheritedDate()
    {
        InheritedDate = _albumInfo?.AlbumDate ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
