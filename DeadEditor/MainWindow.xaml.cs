using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace DeadEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MetadataService _metadataService;
    private readonly NormalizationService _normalizationService;
    private readonly LibraryImportService _libraryImportService;
    private readonly AudioPlayerService _audioPlayer;
    private readonly DispatcherTimer _playbackTimer;
    private LibrarySettings _librarySettings;
    private List<TrackInfo> _tracks = new();
    private AlbumInfo? _albumInfo;
    private bool _isUpdating = false;
    private bool _isScrubbingSeeking = false;
    private int _currentTrackIndex = -1;
    private bool _isManualTrackChange = false;
    private TaskCompletionSource<bool>? _notificationResult;

    public MainWindow()
    {
        InitializeComponent();
        _metadataService = new MetadataService();
        _normalizationService = new NormalizationService();
        _libraryImportService = new LibraryImportService(_metadataService);

        // Initialize audio player
        _audioPlayer = new AudioPlayerService();
        _audioPlayer.PlaybackStopped += AudioPlayer_PlaybackStopped;
        _audioPlayer.PositionChanged += AudioPlayer_PositionChanged;

        // Initialize playback timer for updating scrubber
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _playbackTimer.Start();

        // Load library settings
        _librarySettings = LibrarySettings.Load();

        // Set initial volume
        _audioPlayer.Volume = (float)(VolumeSlider.Value / 100.0);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a folder containing FLAC files",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Folder Selection"
        };

        // Use a workaround to select folders on Windows
        var folderDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder containing FLAC files"
        };

        if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathTextBox.Text = folderDialog.SelectedPath;
            LoadFolder(folderDialog.SelectedPath);
        }
    }

    private async void LoadFolder(string folderPath)
    {
        try
        {
            StatusTextBlock.Text = "Reading files...";

            // Read tracks
            _tracks = _metadataService.ReadFolder(folderPath);

            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Files", "No FLAC files found in the selected folder.");
                StatusTextBlock.Text = "No FLAC files found";
                return;
            }

            // Read album info
            _albumInfo = _metadataService.ReadAlbumInfo(folderPath, _tracks);

            // Update UI
            RefreshUI();

            StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";
        }
        catch (System.Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error loading folder: {ex.Message}");
            StatusTextBlock.Text = "Error loading folder";
        }
    }

    private void RefreshUI()
    {
        _isUpdating = true;

        // Update album info fields
        if (_albumInfo != null)
        {
            ArtistTextBox.Text = _albumInfo.Artist ?? "Grateful Dead";
            DateTextBox.Text = _albumInfo.Date ?? "";
            VenueTextBox.Text = _albumInfo.Venue ?? "";
            CityTextBox.Text = _albumInfo.City ?? "";
            StateTextBox.Text = _albumInfo.State ?? "";
            OfficialReleaseTextBox.Text = _albumInfo.OfficialRelease ?? "";
            UpdateAlbumPreview();
            UpdateArtworkDisplay();
        }

        // Update track list and previews
        UpdateTrackPreviews();
        TracksDataGrid.ItemsSource = null;
        TracksDataGrid.ItemsSource = _tracks;

        _isUpdating = false;
    }

    private void UpdateTrackPreviews()
    {
        if (_albumInfo == null) return;

        foreach (var track in _tracks)
        {
            track.PreviewMetadata = track.GetFinalMetadataTitle(_albumInfo.Date ?? "");
        }
    }

    private void UpdateAlbumPreview()
    {
        if (_albumInfo != null)
        {
            AlbumPreviewTextBlock.Text = _albumInfo.AlbumTitle;
        }
    }

    private void AlbumInfo_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating || _albumInfo == null) return;

        _albumInfo.Artist = ArtistTextBox.Text;
        _albumInfo.Date = DateTextBox.Text;
        _albumInfo.Venue = VenueTextBox.Text;
        _albumInfo.City = CityTextBox.Text;
        _albumInfo.State = StateTextBox.Text;
        _albumInfo.OfficialRelease = OfficialReleaseTextBox.Text;
        _albumInfo.IsModified = true;

        UpdateAlbumPreview();
    }

    private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
        {
            _isUpdating = true;
            SelectedTitleTextBox.Text = selectedTrack.Title;
            SelectedDateTextBox.Text = selectedTrack.PerformanceDate ?? _albumInfo?.Date ?? "";
            SelectedSegueCheckBox.IsChecked = selectedTrack.HasSegue;
            _isUpdating = false;
        }
    }

    private void TracksDataGrid_LoadingRow(object? sender, System.Windows.Controls.DataGridRowEventArgs e)
    {
        UpdateRowBackground(e.Row);
    }

    private void UpdateRowBackground(DataGridRow row)
    {
        if (row.Item is TrackInfo track)
        {
            // Highlight rows with unmatched songs (yellow background)
            if (string.IsNullOrEmpty(track.NormalizedTitle) && !string.IsNullOrEmpty(track.Title))
            {
                // Only highlight after normalization has been attempted
                // We can detect this by checking if any track has a normalized title
                bool normalizationAttempted = _tracks.Any(t => !string.IsNullOrEmpty(t.NormalizedTitle));

                if (normalizationAttempted)
                {
                    row.Background = new SolidColorBrush(Color.FromRgb(255, 255, 200)); // Light yellow
                }
                else
                {
                    row.Background = Brushes.White;
                }
            }
            else
            {
                row.Background = Brushes.White;
            }
        }
    }

    private void UpdateAllRowBackgrounds()
    {
        for (int i = 0; i < TracksDataGrid.Items.Count; i++)
        {
            var row = (DataGridRow?)TracksDataGrid.ItemContainerGenerator.ContainerFromIndex(i);
            if (row != null)
            {
                UpdateRowBackground(row);
            }
        }
    }

    private void SelectedTrack_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
        {
            selectedTrack.Title = SelectedTitleTextBox.Text;
            selectedTrack.PerformanceDate = SelectedDateTextBox.Text;
            selectedTrack.HasSegue = SelectedSegueCheckBox.IsChecked ?? false;
            selectedTrack.IsModified = true;

            // Refresh the DataGrid to show changes
            TracksDataGrid.Items.Refresh();
        }
    }

    private async void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FolderPathTextBox.Text))
        {
            await ShowNotificationAsync("No Folder", "Please select a folder first.");
            return;
        }

        LoadFolder(FolderPathTextBox.Text);
    }

    private async void NormalizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        try
        {
            StatusTextBlock.Text = "Normalizing song titles...";
            int matched = _normalizationService.NormalizeAll(_tracks);

            // Update previews with normalized titles
            UpdateTrackPreviews();

            // Refresh DataGrid to show normalized titles
            TracksDataGrid.Items.Refresh();

            // Update row backgrounds to highlight unmatched songs
            UpdateAllRowBackgrounds();

            // Update selected track if there is one
            if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
            {
                _isUpdating = true;
                SelectedTitleTextBox.Text = selectedTrack.NormalizedTitle ?? selectedTrack.Title;
                _isUpdating = false;
            }

            StatusTextBlock.Text = $"Normalized {matched} of {_tracks.Count} songs";

            if (matched < _tracks.Count)
            {
                await ShowNotificationAsync(
                    "Normalization Complete",
                    $"Matched {matched} of {_tracks.Count} songs.\n\n" +
                    $"{_tracks.Count - matched} songs could not be matched to the database.");
            }
        }
        catch (System.Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error normalizing titles: {ex.Message}");
            StatusTextBlock.Text = "Error during normalization";
        }
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0 || _albumInfo == null)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        var confirmed = await ShowNotificationAsync(
            "Confirm Write",
            $"This will write metadata to {_tracks.Count} FLAC files.\n\n" +
            "This operation cannot be undone. Continue?",
            showYesNo: true);

        if (confirmed)
        {
            try
            {
                StatusTextBlock.Text = "Writing metadata to files...";
                _metadataService.WriteMetadata(_albumInfo, _tracks);
                StatusTextBlock.Text = $"Successfully wrote metadata to {_tracks.Count} files";

                await ShowNotificationAsync(
                    "Success",
                    $"Successfully wrote metadata to {_tracks.Count} files.");

                // Mark as unmodified
                _albumInfo.IsModified = false;
                foreach (var track in _tracks)
                {
                    track.IsModified = false;
                }
            }
            catch (System.Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error writing metadata: {ex.Message}");
                StatusTextBlock.Text = "Error writing metadata";
            }
        }
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(this, _librarySettings);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    // Helper methods for SettingsWindow
    public void UpdateLibraryRootDisplay(string path)
    {
        StatusTextBlock.Text = string.IsNullOrEmpty(path)
            ? "Library root cleared"
            : $"Library root set to: {path}";
    }

    public void ClearCurrentView()
    {
        ClearView();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0 || _albumInfo == null)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
        {
            await ShowNotificationAsync("Library Not Set", "Please set the Library Root folder first.");
            return;
        }

        // Check if show already exists
        if (_libraryImportService.ShowExistsInLibrary(_librarySettings.LibraryRootPath, _albumInfo))
        {
            var overwrite = await ShowNotificationAsync(
                "Show Already Exists",
                $"This show ({_albumInfo.Date} - {_albumInfo.Venue}) already exists in your library.\n\nOverwrite?",
                showYesNo: true);

            if (!overwrite)
            {
                return;
            }
        }

        // Show confirmation dialog
        var confirmed = await ShowNotificationAsync(
            "Confirm Import",
            $"Import {_tracks.Count} tracks to library?\n\n" +
            $"Destination: {_librarySettings.LibraryRootPath}\n" +
            $"Structure: Year\\Date - Venue, City, State\\\n\n" +
            "Original files will NOT be modified.",
            showYesNo: true);

        if (confirmed)
        {
            try
            {
                // Show progress bar
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.Value = 0;
                StatusTextBlock.Text = "Importing to library...";

                // Create progress reporter
                var progress = new System.Progress<(int current, int total, string message)>(report =>
                {
                    ProgressBar.Value = (report.current * 100.0) / report.total;
                    StatusTextBlock.Text = report.message;
                });

                // Import to library with progress reporting (run in background)
                await Task.Run(() =>
                {
                    _libraryImportService.ImportToLibrary(_librarySettings.LibraryRootPath, _albumInfo, _tracks, progress);
                });

                // Hide progress bar
                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Successfully imported {_tracks.Count} tracks to library";

                await ShowNotificationAsync(
                    "Import Complete",
                    $"Successfully imported {_tracks.Count} tracks to library!\n\n" +
                    $"Location: {_librarySettings.LibraryRootPath}\\{System.DateTime.Parse(_albumInfo.Date ?? "2000").Year}\\{_albumInfo.Date} - {_albumInfo.Venue}, {_albumInfo.City}, {_albumInfo.State}");

                // Clear the view after successful import
                ClearView();
            }
            catch (System.Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                await ShowNotificationAsync("Error", $"Error importing to library: {ex.Message}");
                StatusTextBlock.Text = "Error importing to library";
            }
        }
    }

    private void ClearView()
    {
        _tracks.Clear();
        _albumInfo = null;

        // Clear UI fields
        FolderPathTextBox.Text = string.Empty;
        ArtistTextBox.Text = string.Empty;
        DateTextBox.Text = string.Empty;
        VenueTextBox.Text = string.Empty;
        CityTextBox.Text = string.Empty;
        StateTextBox.Text = string.Empty;
        OfficialReleaseTextBox.Text = string.Empty;
        AlbumPreviewTextBlock.Text = string.Empty;
        SelectedTitleTextBox.Text = string.Empty;
        SelectedDateTextBox.Text = string.Empty;
        SelectedSegueCheckBox.IsChecked = false;

        // Clear track list
        TracksDataGrid.ItemsSource = null;

        StatusTextBlock.Text = "Ready";
    }

    private async void BrowseLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
        {
            await ShowNotificationAsync("Library Not Set", "Please set the Library Root folder first.");
            return;
        }

        var browserWindow = new LibraryBrowserWindow(_librarySettings.LibraryRootPath, this);
        browserWindow.Owner = this;
        browserWindow.ShowDialog();
    }

    // Load show from library
    public void LoadShowFromLibrary(string folderPath)
    {
        // Load the show just like loading from file browser
        LoadFolder(folderPath);
    }

    // Artwork management
    private void UpdateArtworkDisplay()
    {
        if (_albumInfo?.ArtworkData != null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(_albumInfo.ArtworkData);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                ArtworkImage.Source = bitmap;
                ArtworkImage.Visibility = Visibility.Visible;
                NoArtworkText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // If artwork fails to load, show no artwork
                ArtworkImage.Source = null;
                ArtworkImage.Visibility = Visibility.Collapsed;
                NoArtworkText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ArtworkImage.Source = null;
            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
        }
    }

    private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null) return;

        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Album Artwork",
            Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                // Read image file
                var imageData = File.ReadAllBytes(openFileDialog.FileName);

                // Determine MIME type
                var extension = Path.GetExtension(openFileDialog.FileName).ToLower();
                var mimeType = extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "image/jpeg"
                };

                // Store artwork in album info
                _albumInfo.ArtworkData = imageData;
                _albumInfo.ArtworkMimeType = mimeType;
                _albumInfo.IsModified = true;

                // Update display
                UpdateArtworkDisplay();

                StatusTextBlock.Text = "Artwork loaded";
            }
            catch (System.Exception ex)
            {
                StatusTextBlock.Text = $"Error loading artwork: {ex.Message}";
            }
        }
    }

    private void RemoveArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null) return;

        _albumInfo.ArtworkData = null;
        _albumInfo.ArtworkMimeType = null;
        _albumInfo.IsModified = true;

        UpdateArtworkDisplay();
        StatusTextBlock.Text = "Artwork removed";
    }

    // Audio Playback Methods
    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            PlayPauseButton.Content = "▶";
        }
        else
        {
            if (_audioPlayer.CurrentFilePath == null)
            {
                // No track loaded, start with first track or selected track
                if (TracksDataGrid.SelectedIndex >= 0)
                {
                    PlayTrack(TracksDataGrid.SelectedIndex);
                }
                else if (_tracks.Count > 0)
                {
                    PlayTrack(0);
                }
            }
            else
            {
                _audioPlayer.Play();
                PlayPauseButton.Content = "⏸";
            }
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _isManualTrackChange = true; // Prevent auto-advance when user manually stops
        _audioPlayer.Stop();
        PlayPauseButton.Content = "▶";
        AudioScrubber.Value = 0;
        NowPlayingText.Text = "No track playing";
        PlaybackTimeText.Text = "00:00 / 00:00";
        _currentTrackIndex = -1;
        _isManualTrackChange = false;
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
        if (_currentTrackIndex < _tracks.Count - 1)
        {
            PlayTrack(_currentTrackIndex + 1);
        }
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;

        var track = _tracks[index];

        // Set flag to prevent auto-advance when changing tracks manually
        _isManualTrackChange = true;
        _currentTrackIndex = index;

        try
        {
            _audioPlayer.LoadFile(track.FilePath);
            _audioPlayer.Play();

            PlayPauseButton.Content = "⏸";
            NowPlayingText.Text = track.DisplayTitle;

            // Select the track in the grid
            TracksDataGrid.SelectedIndex = index;
            TracksDataGrid.ScrollIntoView(track);

            StatusTextBlock.Text = $"Playing: {track.DisplayTitle}";

            // Clear the flag after a short delay to ensure PlaybackStopped event has processed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isManualTrackChange = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error playing track: {ex.Message}";
            _isManualTrackChange = false;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioPlayer != null)
        {
            _audioPlayer.Volume = (float)(e.NewValue / 100.0);
        }
    }

    private void AudioScrubber_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isScrubbingSeeking = true;
    }

    private void AudioScrubber_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isScrubbingSeeking = false;

        // Seek to the new position
        var totalSeconds = _audioPlayer.TotalDuration.TotalSeconds;
        var newPosition = TimeSpan.FromSeconds((AudioScrubber.Value / 100.0) * totalSeconds);
        _audioPlayer.Seek(newPosition);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioPlayer.IsPlaying && !_isScrubbingSeeking)
        {
            _audioPlayer.UpdatePosition();

            // Update scrubber
            var totalSeconds = _audioPlayer.TotalDuration.TotalSeconds;
            if (totalSeconds > 0)
            {
                var percentage = (_audioPlayer.CurrentPosition.TotalSeconds / totalSeconds) * 100.0;
                AudioScrubber.Value = percentage;
            }

            // Update time display
            var current = _audioPlayer.CurrentPosition.ToString(@"mm\:ss");
            var total = _audioPlayer.TotalDuration.ToString(@"mm\:ss");
            PlaybackTimeText.Text = $"{current} / {total}";
        }
    }

    private void AudioPlayer_PositionChanged(object? sender, TimeSpan e)
    {
        // This event is fired from AudioPlayerService, but we update in the timer instead
    }

    private void AudioPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Don't auto-advance if we're manually changing tracks
            if (_isManualTrackChange)
            {
                return;
            }

            PlayPauseButton.Content = "▶";

            // Auto-play next track if available (only when track finishes naturally)
            if (_currentTrackIndex < _tracks.Count - 1)
            {
                PlayTrack(_currentTrackIndex + 1);
            }
            else
            {
                // End of playlist
                AudioScrubber.Value = 0;
                NowPlayingText.Text = "Playlist finished";
                PlaybackTimeText.Text = "00:00 / 00:00";
                _currentTrackIndex = -1;
            }
        });
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.Item is TrackInfo track)
        {
            int index = _tracks.IndexOf(track);
            if (index >= 0)
            {
                PlayTrack(index);
                e.Handled = true; // Prevent event from bubbling
            }
        }
    }

    // Notification system to replace MessageBox
    private Task<bool> ShowNotificationAsync(string title, string message, bool showYesNo = false)
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

        return _notificationResult.Task;
    }

    private void HideNotification()
    {
        NotificationPanel.Visibility = Visibility.Collapsed;
    }

    private void NotificationYes_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(true);
    }

    private void NotificationNo_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(false);
    }

    private void NotificationOk_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(true);
    }

    private void NotificationPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close notification if clicking outside the dialog
        if (e.OriginalSource == NotificationPanel)
        {
            HideNotification();
            _notificationResult?.SetResult(false);
        }
    }
}
