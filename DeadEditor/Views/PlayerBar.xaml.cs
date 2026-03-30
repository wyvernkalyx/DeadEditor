using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeadEditor
{
    public partial class PlayerBar : System.Windows.Controls.UserControl
    {
        private readonly AudioPlayerService _player;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _marqueeTimer;
        private bool _isSeeking = false;
        private double _marqueePosition = 0;

        public PlayerBar()
        {
            InitializeComponent();

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

            // Initialize UI from current playback state
            UpdatePlaybackUI();
            UpdateTrackUI();

            // Restore persisted volume
            var settings = LibrarySettings.Load();
            VolumeSlider.Value = settings.VolumePercent;
            _player.Volume = (float)(settings.VolumePercent / 100.0);

            // Subscribe to unloaded event to cleanup
            Unloaded += PlayerBar_Unloaded;
        }

        private void PlayerBar_Unloaded(object sender, RoutedEventArgs e)
        {
            // Cleanup when control is unloaded
            _player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            _player.TrackChanged -= Player_TrackChanged;
            _updateTimer.Stop();
            _marqueeTimer.Stop();
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

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Next();
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

                // Persist volume setting
                var settings = LibrarySettings.Load();
                settings.VolumePercent = (int)e.NewValue;
                settings.Save();
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
                    PlayButtonIcon.Text = "\uE769";  // Pause glyph
                    _updateTimer.Start();
                    break;

                case PlaybackState.Paused:
                    PlayButtonIcon.Text = "\uE768";  // Play glyph
                    _updateTimer.Stop();
                    break;

                case PlaybackState.Stopped:
                    PlayButtonIcon.Text = "\uE768";  // Play glyph
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

                // Use DisplayTitle which already formats "Song > (Date)" correctly
                MarqueeText.Text = track.DisplayTitle;

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
                var position = _player.CurrentPosition;
                var duration = _player.TotalDuration;
                var clampedPosition = position > duration ? duration : position;

                CurrentTimeText.Text = FormatTime(clampedPosition);

                if (duration.TotalSeconds > 0)
                {
                    SeekSlider.Value = Math.Min(100, (clampedPosition.TotalSeconds / duration.TotalSeconds) * 100);
                }
            }
        }

        // ===== MARQUEE ANIMATION =====

        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            var textWidth = MeasureTextWidth(MarqueeText.Text, MarqueeText);
            var canvasWidth = MarqueeCanvas.ActualWidth;

            if (textWidth > canvasWidth)
            {
                _marqueePosition -= 1;

                if (_marqueePosition < -textWidth)
                {
                    _marqueePosition = canvasWidth;
                }

                MarqueeTransform.X = _marqueePosition;
            }
            else
            {
                MarqueeTransform.X = (canvasWidth - textWidth) / 2;
            }
        }

        private double MeasureTextWidth(string text, TextBlock textBlock)
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
                96);

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
    }
}
