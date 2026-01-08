using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor;

public partial class ConcertDetailWindow : Window
{
    private readonly string _concertFolderPath;
    private readonly MetadataService _metadataService;
    private readonly AudioPlayerService _audioPlayer;
    private List<TrackInfo> _tracks = new();
    private int _currentTrackIndex = -1;
    private DispatcherTimer? _updateTimer;
    private bool _isScrubbing = false;
    private bool _isManualTrackChange = false;

    public ConcertDetailWindow(string concertFolderPath, string concertTitle)
    {
        InitializeComponent();

        _concertFolderPath = concertFolderPath;
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

        // Set window title
        Title = concertTitle;

        // Load concert data
        LoadConcertData();
    }

    private void LoadConcertData()
    {
        try
        {
            // Parse folder name: "yyyy-MM-dd - Venue, City, State"
            var folderName = Path.GetFileName(_concertFolderPath);
            var parts = folderName.Split(new[] { " - " }, 2, StringSplitOptions.None);

            if (parts.Length == 2)
            {
                var date = parts[0];
                var venueParts = parts[1].Split(new[] { ", " }, StringSplitOptions.None);

                var venue = venueParts.Length > 0 ? venueParts[0] : "";
                var location = venueParts.Length > 1 ? string.Join(", ", venueParts.Skip(1)) : "";

                VenueText.Text = venue;
                LocationText.Text = location;
                DateText.Text = date;
            }

            // Load artwork
            var artworkPath = Path.Combine(_concertFolderPath, "cover.jpg");
            if (!File.Exists(artworkPath))
            {
                artworkPath = Path.Combine(_concertFolderPath, "folder.jpg");
            }

            if (File.Exists(artworkPath))
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                bitmap.EndInit();
                AlbumArtwork.Source = bitmap;

                // Hide placeholder when artwork loads
                ArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Show placeholder when no artwork
                ArtworkPlaceholder.Visibility = Visibility.Visible;
            }

            // Load tracks
            _tracks = _metadataService.ReadFolder(_concertFolderPath);
            TracksDataGrid.ItemsSource = _tracks;
            TrackCountText.Text = $"{_tracks.Count} tracks";

            // Enable play button if we have tracks
            if (_tracks.Count > 0)
            {
                PlayPauseButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading concert data: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TracksDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.MouseDoubleClick += TracksDataGridRow_MouseDoubleClick;
    }

    private void TracksDataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.Item is TrackInfo track)
        {
            var index = _tracks.IndexOf(track);
            if (index >= 0)
            {
                PlayTrack(index);
            }
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

            // Update UI
            PlayPauseButton.Content = "⏸";
            PlayPauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            PreviousButton.IsEnabled = _currentTrackIndex > 0;
            NextButton.IsEnabled = _currentTrackIndex < _tracks.Count - 1;

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
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _tracks.Count)
            {
                _audioPlayer.Play();
                PlayPauseButton.Content = "⏸";
                _updateTimer?.Start();
            }
            else if (_tracks.Count > 0)
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
        PlayPauseButton.IsEnabled = _tracks.Count > 0;
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

    private void AudioPlayer_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _updateTimer?.Stop();
            PlayPauseButton.Content = "▶";

            // Auto-advance to next track if not manually changing tracks
            if (!_isManualTrackChange && _currentTrackIndex >= 0 && _currentTrackIndex < _tracks.Count - 1)
            {
                PlayTrack(_currentTrackIndex + 1);
            }
            else if (_currentTrackIndex >= _tracks.Count - 1)
            {
                // End of playlist
                AudioScrubber.Value = 0;
                CurrentTimeText.Text = "0:00";
                NowPlayingText.Text = "Playlist ended";
                NowPlayingDetails.Text = "";
                StopButton.IsEnabled = false;
                PlayPauseButton.IsEnabled = _tracks.Count > 0;
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _audioPlayer?.Dispose();
        _updateTimer?.Stop();
        base.OnClosing(e);
    }
}
