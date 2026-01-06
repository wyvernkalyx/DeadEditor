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
    private readonly AudioPlayerService _audioPlayer;
    private List<LibraryShow> _shows = new();
    private List<TrackInfo> _currentTracks = new();
    private LibraryShow? _currentShow = null;
    private int _currentTrackIndex = -1;
    private DispatcherTimer? _updateTimer;
    private bool _isScrubbing = false;
    private bool _isManualTrackChange = false;

    public LibraryBrowserWindow()
    {
        InitializeComponent();

        _librarySettings = LibrarySettings.Load();
        _metadataService = new MetadataService();
        _audioPlayer = new AudioPlayerService();

        // Set up audio player events
        _audioPlayer.PlaybackStopped += AudioPlayer_PlaybackStopped;

        // Set up update timer for scrubber
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Load shows
        LoadShows();
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

        if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath) ||
            !Directory.Exists(_librarySettings.LibraryRootPath))
        {
            ShowsDataGrid.ItemsSource = _shows;
            StatusText.Text = "No library path set. Go to File > Settings to configure.";
            return;
        }

        // Scan year folders
        var yearFolders = Directory.GetDirectories(_librarySettings.LibraryRootPath);

        foreach (var yearFolder in yearFolders)
        {
            var showFolders = Directory.GetDirectories(yearFolder);

            foreach (var showFolder in showFolders)
            {
                var folderName = Path.GetFileName(showFolder);

                // Expected format: "yyyy-MM-dd - Venue, City, State"
                var parts = folderName.Split(new[] { " - " }, 2, StringSplitOptions.None);

                if (parts.Length == 2)
                {
                    var date = parts[0];
                    var venueParts = parts[1].Split(new[] { ", " }, StringSplitOptions.None);

                    var venue = venueParts.Length > 0 ? venueParts[0] : "";
                    var city = venueParts.Length > 1 ? venueParts[1] : "";
                    var state = venueParts.Length > 2 ? venueParts[2] : "";

                    var flacFiles = Directory.GetFiles(showFolder, "*.flac");

                    _shows.Add(new LibraryShow
                    {
                        Date = date,
                        Venue = venue,
                        City = city,
                        State = state,
                        Location = venueParts.Length > 1 ? string.Join(", ", venueParts.Skip(1)) : "",
                        TrackCount = flacFiles.Length,
                        FolderPath = showFolder
                    });
                }
            }
        }

        // Sort by date descending
        _shows = _shows.OrderByDescending(s => s.Date).ToList();

        ShowsDataGrid.ItemsSource = _shows;
        StatusText.Text = $"{_shows.Count} concerts in library";
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

            // Load concert info
            VenueText.Text = show.Venue;
            LocationText.Text = show.Location;
            DateText.Text = show.Date;

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
                // Try to extract embedded artwork from first FLAC file
                var flacFiles = Directory.GetFiles(show.FolderPath, "*.flac");
                if (flacFiles.Length > 0)
                {
                    try
                    {
                        var tagFile = TagLib.File.Create(flacFiles[0]);
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
                track.PreviewMetadata = track.GetFinalMetadataTitle(show.Date);
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
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            PlayPauseButton.Content = "▶";
            _updateTimer?.Stop();
        }
        else
        {
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
        var settingsWindow = new SettingsWindow(this, _librarySettings);
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
}

public class LibraryShow
{
    public string Date { get; set; } = "";
    public string Venue { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Location { get; set; } = "";
    public int TrackCount { get; set; }
    public string FolderPath { get; set; } = "";
}
