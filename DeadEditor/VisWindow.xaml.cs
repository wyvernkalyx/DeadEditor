using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    /// <summary>
    /// Visualizer stub window. Architecture identical to PlaylistWindow for future replacement.
    /// </summary>
    public partial class VisWindow : Window
    {
        private readonly PlaylistWindow _playlistWindow;
        private bool _isAttached = true;
        private readonly Models.LibrarySettings _settings;

        public VisWindow(PlaylistWindow playlistWindow)
        {
            InitializeComponent();

            _playlistWindow = playlistWindow ?? throw new ArgumentNullException(nameof(playlistWindow));
            _settings = Models.LibrarySettings.Load();

            // Attach to PlaylistWindow by default (or restore detached position)
            RestoreWindowPosition();

            // Save position when window moves (only when detached)
            LocationChanged += VisWindow_LocationChangedSave;
        }

        /// <summary>
        /// Restores window position from settings, or attaches below PlaylistWindow by default.
        /// </summary>
        private void RestoreWindowPosition()
        {
            var left = _settings.VisWindowLeft;
            var top = _settings.VisWindowTop;

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
                    _settings.VisWindowLeft = null;
                    _settings.VisWindowTop = null;
                    _settings.Save();
                }
                // Default: attach below PlaylistWindow
                AttachToPlaylistWindow();
            }
        }

        /// <summary>
        /// Saves window position when user moves the window (only when detached).
        /// </summary>
        private void VisWindow_LocationChangedSave(object? sender, EventArgs e)
        {
            // Only save if detached and window is in normal state
            if (!_isAttached && WindowState == WindowState.Normal && Left >= 0 && Top >= 0)
            {
                _settings.VisWindowLeft = Left;
                _settings.VisWindowTop = Top;
                _settings.Save();
            }
        }

        /// <summary>
        /// Attaches the visualizer window below PlaylistWindow (subscribes to position events).
        /// </summary>
        public void AttachToPlaylistWindow()
        {
            _isAttached = true;

            // Clear saved position (attached windows don't need saved position)
            _settings.VisWindowLeft = null;
            _settings.VisWindowTop = null;
            _settings.Save();

            // Subscribe to PlaylistWindow position/size changes
            _playlistWindow.LocationChanged += PlaylistWindow_LocationChanged;
            _playlistWindow.SizeChanged += PlaylistWindow_SizeChanged;

            // Initial position snap
            SnapToPlaylistWindow();
        }

        /// <summary>
        /// Detaches the visualizer window (unsubscribes from position events).
        /// </summary>
        public void DetachFromPlaylistWindow()
        {
            _isAttached = false;

            // Unsubscribe from PlaylistWindow events
            _playlistWindow.LocationChanged -= PlaylistWindow_LocationChanged;
            _playlistWindow.SizeChanged -= PlaylistWindow_SizeChanged;
        }

        /// <summary>
        /// Snaps VisWindow to directly below PlaylistWindow.
        /// </summary>
        private void SnapToPlaylistWindow()
        {
            Left = _playlistWindow.Left;
            Top = _playlistWindow.Top + _playlistWindow.ActualHeight;
        }

        private void PlaylistWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                SnapToPlaylistWindow();
            }
        }

        private void PlaylistWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isAttached)
            {
                SnapToPlaylistWindow();
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
                DetachFromPlaylistWindow();
            }
            else
            {
                AttachToPlaylistWindow();
            }
        }

        // ===== CLEANUP =====

        protected override void OnClosing(CancelEventArgs e)
        {
            LocationChanged -= VisWindow_LocationChangedSave;

            if (_isAttached)
            {
                _playlistWindow.LocationChanged -= PlaylistWindow_LocationChanged;
                _playlistWindow.SizeChanged -= PlaylistWindow_SizeChanged;
            }

            base.OnClosing(e);
        }
    }
}
