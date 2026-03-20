using DeadEditor.Models;
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
        private readonly AudioPlayerService _player;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _marqueeTimer;
        private bool _isSeeking = false;
        private double _marqueePosition = 0;
        private readonly LibrarySettings _settings;

        // PlaylistWindow and VisWindow references (set by App.xaml.cs)
        internal PlaylistWindow? PlaylistWindowInstance { get; set; }
        internal VisWindow? VisWindowInstance { get; set; }

        public PlayerWindow()
        {
            InitializeComponent();

            _player = App.PlaybackService;
            _settings = LibrarySettings.Load();

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

            // Restore window position or set default
            RestoreWindowPosition();

            // Save position when window moves
            LocationChanged += PlayerWindow_LocationChanged;

            // Initialize UI from current playback state
            UpdatePlaybackUI();
            UpdateTrackUI();
        }

        /// <summary>
        /// Restores window position from settings, or sets default position if no saved position.
        /// </summary>
        private void RestoreWindowPosition()
        {
            var left = _settings.PlayerWindowLeft;
            var top = _settings.PlayerWindowTop;

            System.Diagnostics.Debug.WriteLine($"[PlayerWindow] Restoring position: Left={left}, Top={top}");

            // Check if any monitor contains this position
            bool isOnScreen = false;
            if (left.HasValue && top.HasValue)
            {
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var workArea = screen.WorkingArea;
                    System.Diagnostics.Debug.WriteLine($"[PlayerWindow] Checking screen: {workArea}");

                    // Check if the top-left corner of the window is within this screen's working area
                    if (left.Value >= workArea.Left && left.Value < workArea.Right &&
                        top.Value >= workArea.Top && top.Value < workArea.Bottom)
                    {
                        isOnScreen = true;
                        System.Diagnostics.Debug.WriteLine($"[PlayerWindow] Position is on screen");
                        break;
                    }
                }
            }

            if (isOnScreen && left.HasValue && top.HasValue && left.Value != 0 && top.Value != 0)
            {
                Left = left.Value;
                Top = top.Value;
                System.Diagnostics.Debug.WriteLine($"[PlayerWindow] Restored to saved position: Left={Left}, Top={Top}");
            }
            else
            {
                // Default: center-bottom of primary screen
                var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                Left = screen.Left + (screen.Width - Width) / 2;
                Top = screen.Top + screen.Height - Height - 40; // 40px from bottom for taskbar

                System.Diagnostics.Debug.WriteLine($"[PlayerWindow] Using default position: Left={Left}, Top={Top}");

                // Clear bad saved values
                _settings.PlayerWindowLeft = Left;
                _settings.PlayerWindowTop = Top;
                _settings.Save();
            }
        }

        /// <summary>
        /// Saves window position when user moves the window.
        /// </summary>
        private void PlayerWindow_LocationChanged(object? sender, EventArgs e)
        {
            // Don't save if window is minimized or maximized
            if (WindowState == WindowState.Normal && Left >= 0 && Top >= 0)
            {
                _settings.PlayerWindowLeft = Left;
                _settings.PlayerWindowTop = Top;
                _settings.Save();
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
            LocationChanged -= PlayerWindow_LocationChanged;

            _updateTimer.Stop();
            _marqueeTimer.Stop();

            base.OnClosing(e);
        }
    }
}
