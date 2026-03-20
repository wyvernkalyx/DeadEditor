using DeadEditor.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeadEditor
{
    public partial class PlayerWindow : Window
    {
        private readonly Window _parentWindow;
        private readonly AudioPlayerService _player;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _marqueeTimer;
        private bool _isDocked = true;
        private bool _isSeeking = false;
        private double _marqueePosition = 0;

        // PlaylistWindow and VisWindow references (set by App.xaml.cs)
        internal PlaylistWindow? PlaylistWindowInstance { get; set; }
        internal VisWindow? VisWindowInstance { get; set; }

        public PlayerWindow(Window parentWindow)
        {
            InitializeComponent();

            _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            _player = App.PlaybackService;

            // Subscribe to playback service events
            _player.PlaybackStateChanged += Player_PlaybackStateChanged;
            _player.TrackChanged += Player_TrackChanged;

            // Set up update timer for seek bar
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            // Set up marquee animation timer
            _marqueeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _marqueeTimer.Start();

            // Subscribe to MainWindow events for docking
            DockToMainWindow();

            // Initialize UI from current playback state
            UpdatePlaybackUI();
            UpdateTrackUI();
        }

        /// <summary>
        /// Docks the player window to MainWindow (subscribes to position events).
        /// </summary>
        public void DockToMainWindow()
        {
            _isDocked = true;

            // Subscribe to parent window position/size changes
            _parentWindow.LocationChanged += MainWindow_LocationChanged;
            _parentWindow.SizeChanged += MainWindow_SizeChanged;

            // Initial position snap
            SnapToMainWindow();
        }

        /// <summary>
        /// Undocks the player window (unsubscribes from position events).
        /// </summary>
        public void UndockFromMainWindow()
        {
            _isDocked = false;

            // Unsubscribe from parent window events
            _parentWindow.LocationChanged -= MainWindow_LocationChanged;
            _parentWindow.SizeChanged -= MainWindow_SizeChanged;
        }

        /// <summary>
        /// Snaps PlayerWindow to bottom-left of parent window.
        /// </summary>
        private void SnapToMainWindow()
        {
            Left = _parentWindow.Left;
            Top = _parentWindow.Top + _parentWindow.ActualHeight;
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_isDocked)
            {
                SnapToMainWindow();
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isDocked)
            {
                SnapToMainWindow();
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

        private void UndockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDocked)
            {
                UndockFromMainWindow();
                // TODO: Phase 3 - Update placeholder bar when implemented
                // if (_parentWindow is LibraryBrowserWindow libraryWindow)
                // {
                //     libraryWindow.UpdatePlayerPlaceholderBar(isDocked: false);
                // }
            }
            else
            {
                DockToMainWindow();
                // TODO: Phase 3 - Update placeholder bar when implemented
                // if (_parentWindow is LibraryBrowserWindow libraryWindow)
                // {
                //     libraryWindow.UpdatePlayerPlaceholderBar(isDocked: true);
                // }
            }
        }

        // ===== TRANSPORT CONTROLS =====

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Previous();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player.State == PlaybackState.Playing)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Pause();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Next();
        }

        private void PlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistWindowInstance != null)
            {
                PlaylistWindowInstance.Visibility = PlaylistWindowInstance.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void VisButton_Click(object sender, RoutedEventArgs e)
        {
            if (VisWindowInstance != null)
            {
                VisWindowInstance.Visibility = VisWindowInstance.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        // ===== SEEK BAR =====

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = false;
            var position = TimeSpan.FromSeconds((_player.TotalDuration.TotalSeconds * SeekSlider.Value) / 100);
            _player.Seek(position);
        }

        // ===== VOLUME =====

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
            {
                _player.Volume = (float)(e.NewValue / 100.0);
            }
        }

        // ===== PLAYBACK STATE UPDATES =====

        private void Player_PlaybackStateChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePlaybackUI();
            });
        }

        private void Player_TrackChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrackUI();
            });
        }

        private void UpdatePlaybackUI()
        {
            switch (_player.State)
            {
                case PlaybackState.Playing:
                    PlayButton.Content = "⏸";  // Show pause when playing
                    _updateTimer.Start();
                    break;

                case PlaybackState.Paused:
                    PlayButton.Content = "▶";  // Show play when paused
                    _updateTimer.Stop();
                    break;

                case PlaybackState.Stopped:
                    PlayButton.Content = "▶";  // Show play when stopped
                    _updateTimer.Stop();
                    SeekSlider.Value = 0;
                    CurrentTimeText.Text = "0:00";
                    break;
            }
        }

        private void UpdateTrackUI()
        {
            if (_player.CurrentTrack != null)
            {
                var track = _player.CurrentTrack;

                // Update marquee text
                // Format: "Song Name (yyyy-MM-dd)"
                var songName = track.SongName ?? track.Title;
                var date = track.TrackDate ?? "";

                if (!string.IsNullOrEmpty(date))
                {
                    MarqueeText.Text = $"{songName} ({date})";
                }
                else
                {
                    MarqueeText.Text = songName;
                }

                // Update total time
                TotalTimeText.Text = FormatTime(_player.TotalDuration);

                // Reset marquee position
                _marqueePosition = 0;
                MarqueeTransform.X = 0;
            }
            else
            {
                MarqueeText.Text = "No track playing";
                TotalTimeText.Text = "0:00";
                _marqueePosition = 0;
                MarqueeTransform.X = 0;
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isSeeking && _player.State == PlaybackState.Playing)
            {
                CurrentTimeText.Text = FormatTime(_player.CurrentPosition);

                if (_player.TotalDuration.TotalSeconds > 0)
                {
                    SeekSlider.Value = (_player.CurrentPosition.TotalSeconds / _player.TotalDuration.TotalSeconds) * 100;
                }
            }
        }

        // ===== MARQUEE ANIMATION =====

        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            // Measure text width
            var textWidth = MeasureTextWidth(MarqueeText.Text, MarqueeText);
            var canvasWidth = MarqueeCanvas.ActualWidth;

            // Only scroll if text is wider than canvas
            if (textWidth > canvasWidth)
            {
                _marqueePosition -= 1;  // Scroll left by 1 pixel

                // Reset when text has scrolled completely off screen
                if (_marqueePosition < -textWidth)
                {
                    _marqueePosition = canvasWidth;
                }

                MarqueeTransform.X = _marqueePosition;
            }
            else
            {
                // Center text if it fits
                MarqueeTransform.X = (canvasWidth - textWidth) / 2;
            }
        }

        private double MeasureTextWidth(string text, System.Windows.Controls.TextBlock textBlock)
        {
            var formattedText = new System.Windows.Media.FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                textBlock.FontSize,
                textBlock.Foreground,
                new System.Windows.Media.NumberSubstitution(),
                System.Windows.Media.TextFormattingMode.Display,
                96);  // 96 DPI

            return formattedText.Width;
        }

        private string FormatTime(TimeSpan time)
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Unsubscribe from events
            _player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            _player.TrackChanged -= Player_TrackChanged;

            if (_isDocked)
            {
                _parentWindow.LocationChanged -= MainWindow_LocationChanged;
                _parentWindow.SizeChanged -= MainWindow_SizeChanged;
            }

            _updateTimer.Stop();
            _marqueeTimer.Stop();

            base.OnClosing(e);
        }
    }
}
