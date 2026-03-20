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

        public VisWindow(PlaylistWindow playlistWindow)
        {
            InitializeComponent();

            _playlistWindow = playlistWindow ?? throw new ArgumentNullException(nameof(playlistWindow));

            // Attach to PlaylistWindow by default
            AttachToPlaylistWindow();
        }

        /// <summary>
        /// Attaches the visualizer window below PlaylistWindow (subscribes to position events).
        /// </summary>
        public void AttachToPlaylistWindow()
        {
            _isAttached = true;

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
            if (_isAttached)
            {
                _playlistWindow.LocationChanged -= PlaylistWindow_LocationChanged;
                _playlistWindow.SizeChanged -= PlaylistWindow_SizeChanged;
            }

            base.OnClosing(e);
        }
    }
}
