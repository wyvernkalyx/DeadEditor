using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    /// <summary>
    /// Wrapper for TrackInfo that adds computed properties for playlist display.
    /// </summary>
    public class PlaylistTrackViewModel : INotifyPropertyChanged
    {
        private readonly TrackInfo _track;
        private readonly AudioPlayerService _player;

        public PlaylistTrackViewModel(TrackInfo track, AudioPlayerService player)
        {
            _track = track;
            _player = player;
        }

        public TrackInfo Track => _track;

        public string TrackNumber => _track.TrackNumber.ToString();

        public string DisplayTitle => _track.SongName ?? _track.Title ?? "";

        public string ShortDate
        {
            get
            {
                if (string.IsNullOrEmpty(_track.TrackDate))
                    return "";

                // Return full yyyy-MM-dd format
                return _track.TrackDate;
            }
        }

        public string Duration => _track.Duration ?? "";

        public bool IsCurrentTrack => _player.CurrentTrack == _track;

        public void RefreshIsCurrentTrack()
        {
            OnPropertyChanged(nameof(IsCurrentTrack));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class PlaylistWindow : Window
    {
        private readonly PlayerWindow _playerWindow;
        private readonly AudioPlayerService _player;
        private bool _isAttached = true;
        private readonly ObservableCollection<PlaylistTrackViewModel> _playlistViewModels;
        private readonly LibrarySettings _settings;

        public PlaylistWindow(PlayerWindow playerWindow)
        {
            InitializeComponent();

            _playerWindow = playerWindow ?? throw new ArgumentNullException(nameof(playerWindow));
            _player = App.PlaybackService;
            _settings = LibrarySettings.Load();

            // Create view model collection
            _playlistViewModels = new ObservableCollection<PlaylistTrackViewModel>();
            PlaylistDataGrid.ItemsSource = _playlistViewModels;

            // Subscribe to playlist changes
            _player.Playlist.CollectionChanged += Playlist_CollectionChanged;

            // Subscribe to track changes to update highlighting
            _player.TrackChanged += Player_TrackChanged;

            // Initialize playlist view models from current playlist
            RebuildPlaylistViewModels();

            // Attach to PlayerWindow by default (or restore detached position)
            RestoreWindowPosition();

            // Save position when window moves (only when detached)
            LocationChanged += PlaylistWindow_LocationChangedSave;

            // Update footer stats
            UpdateFooterStats();
        }

        /// <summary>
        /// Restores window position from settings, or attaches below PlayerWindow by default.
        /// </summary>
        private void RestoreWindowPosition()
        {
            var left = _settings.PlaylistWindowLeft;
            var top = _settings.PlaylistWindowTop;

            // Check if saved position is on screen
            bool isOnScreen = false;
            if (left.HasValue && top.HasValue)
            {
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var workArea = screen.WorkingArea;
                    if (left.Value >= workArea.Left && left.Value < workArea.Right &&
                        top.Value >= workArea.Top && top.Value < workArea.Bottom)
                    {
                        isOnScreen = true;
                        break;
                    }
                }
            }

            if (isOnScreen && left.HasValue && top.HasValue)
            {
                // Restore saved position (implies detached)
                _isAttached = false;
                Left = left.Value;
                Top = top.Value;
            }
            else
            {
                // Clear bad saved values if any
                if (left.HasValue || top.HasValue)
                {
                    _settings.PlaylistWindowLeft = null;
                    _settings.PlaylistWindowTop = null;
                    _settings.Save();
                }
                // Default: attach below PlayerWindow
                AttachToPlayerWindow();
            }
        }

        /// <summary>
        /// Saves window position when user moves the window (only when detached).
        /// </summary>
        private void PlaylistWindow_LocationChangedSave(object? sender, EventArgs e)
        {
            // Only save if detached and window is in normal state
            if (!_isAttached && WindowState == WindowState.Normal && Left >= 0 && Top >= 0)
            {
                _settings.PlaylistWindowLeft = Left;
                _settings.PlaylistWindowTop = Top;
                _settings.Save();
            }
        }

        /// <summary>
        /// Attaches the playlist window below PlayerWindow (subscribes to position events).
        /// </summary>
        public void AttachToPlayerWindow()
        {
            _isAttached = true;

            // Clear saved position (attached windows don't need saved position)
            _settings.PlaylistWindowLeft = null;
            _settings.PlaylistWindowTop = null;
            _settings.Save();

            // Subscribe to PlayerWindow position/size changes
            _playerWindow.LocationChanged += PlayerWindow_LocationChanged;
            _playerWindow.SizeChanged += PlayerWindow_SizeChanged;

            // Initial position snap
            SnapToPlayerWindow();
        }

        /// <summary>
        /// Detaches the playlist window (unsubscribes from position events).
        /// </summary>
        public void DetachFromPlayerWindow()
        {
            _isAttached = false;

            // Unsubscribe from PlayerWindow events
            _playerWindow.LocationChanged -= PlayerWindow_LocationChanged;
            _playerWindow.SizeChanged -= PlayerWindow_SizeChanged;
        }

        /// <summary>
        /// Snaps PlaylistWindow to directly below PlayerWindow.
        /// </summary>
        private void SnapToPlayerWindow()
        {
            Left = _playerWindow.Left;
            Top = _playerWindow.Top + _playerWindow.ActualHeight;
        }

        private void PlayerWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                SnapToPlayerWindow();
            }
        }

        private void PlayerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isAttached)
            {
                SnapToPlayerWindow();
            }
        }

        // ===== PLAYLIST SYNCHRONIZATION =====

        private void Playlist_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RebuildPlaylistViewModels();
                UpdateFooterStats();
            });
        }

        private void RebuildPlaylistViewModels()
        {
            _playlistViewModels.Clear();

            foreach (var track in _player.Playlist)
            {
                _playlistViewModels.Add(new PlaylistTrackViewModel(track, _player));
            }
        }

        private void Player_TrackChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Refresh IsCurrentTrack for all rows
                foreach (var vm in _playlistViewModels)
                {
                    vm.RefreshIsCurrentTrack();
                }
            });
        }

        private void UpdateFooterStats()
        {
            var trackCount = _player.Playlist.Count;

            // Calculate total duration
            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (var track in _player.Playlist)
            {
                if (!string.IsNullOrEmpty(track.Duration) && TimeSpan.TryParse(track.Duration, out var duration))
                {
                    totalDuration += duration;
                }
            }

            var durationText = FormatTotalDuration(totalDuration);
            FooterStatsText.Text = $"{trackCount} tracks  {durationText}";
        }

        private string FormatTotalDuration(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }
            else
            {
                return $"{time.Minutes}:{time.Seconds:D2}";
            }
        }

        // ===== DRAG BAR =====

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void DetachButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAttached)
            {
                DetachFromPlayerWindow();
            }
            else
            {
                AttachToPlayerWindow();
            }
        }

        // ===== PLAYLIST ACTIONS =====

        private void PlaylistDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PlaylistDataGrid.SelectedItem is PlaylistTrackViewModel vm)
            {
                // Guard: Verify playlist not empty and contains the track
                if (_player.Playlist.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[PlaylistWindow] Cannot play - playlist is empty");
                    return;
                }

                if (!_player.Playlist.Contains(vm.Track))
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaylistWindow] WARNING: Track '{vm.Track.SongName ?? vm.Track.Title}' not found in playlist");
                    return;
                }

                _player.Play(vm.Track);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear playlist only - do not stop playback
            // The currently playing track continues playing even with an empty playlist
            _player.Playlist.Clear();
        }

        // ===== CLEANUP =====

        protected override void OnClosing(CancelEventArgs e)
        {
            // Unsubscribe from events
            _player.Playlist.CollectionChanged -= Playlist_CollectionChanged;
            _player.TrackChanged -= Player_TrackChanged;
            LocationChanged -= PlaylistWindow_LocationChangedSave;

            if (_isAttached)
            {
                _playerWindow.LocationChanged -= PlayerWindow_LocationChanged;
                _playerWindow.SizeChanged -= PlayerWindow_SizeChanged;
            }

            base.OnClosing(e);
        }
    }
}
