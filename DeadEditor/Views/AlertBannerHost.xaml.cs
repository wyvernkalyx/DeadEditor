using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DeadEditor.Services;
using Color = System.Windows.Media.Color;

namespace DeadEditor.Views
{
    /// <summary>
    /// The shell's in-window alert banner (alert-system-spec.md Ruling 1). Implements
    /// <see cref="IAlertSink"/>; the <see cref="AlertService"/> forwards notifications here on the
    /// UI thread. Owns the visuals and the auto-dismiss timer; the queue/dismissal ordering and
    /// the severity policy live in the pure <see cref="AlertQueue"/>.
    /// </summary>
    public partial class AlertBannerHost : System.Windows.Controls.UserControl, IAlertSink
    {
        private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(5);

        private readonly AlertQueue _queue = new();
        private readonly DispatcherTimer _autoDismissTimer;

        public AlertBannerHost()
        {
            InitializeComponent();
            _autoDismissTimer = new DispatcherTimer { Interval = AutoDismissDelay };
            _autoDismissTimer.Tick += AutoDismissTimer_Tick;
        }

        /// <summary>IAlertSink — always invoked on the UI thread (the service marshals).</summary>
        public void Show(AlertItem item)
        {
            var toShow = _queue.Enqueue(item);
            if (toShow != null)
                Render(toShow);
            // else: a banner is already showing; this one waits its turn (surfaces on dismiss).
        }

        private void Render(AlertItem item)
        {
            _autoDismissTimer.Stop();

            MessageText.Text = item.Message;
            SeverityLabel.Text = item.Title ?? DefaultLabel(item.Severity);
            ApplySeverityStyle(item.Severity);

            BannerBorder.Visibility = Visibility.Visible;

            if (AlertQueue.ShouldAutoDismiss(item.Severity))
                _autoDismissTimer.Start();
        }

        private void AdvanceOrHide()
        {
            _autoDismissTimer.Stop();
            var next = _queue.Dismiss();
            if (next != null)
                Render(next);
            else
                BannerBorder.Visibility = Visibility.Collapsed;
        }

        private void AutoDismissTimer_Tick(object? sender, EventArgs e) => AdvanceOrHide();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => AdvanceOrHide();

        private static string DefaultLabel(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Warning => "Warning",
            AlertSeverity.Error => "Error",
            _ => "Info"
        };

        private void ApplySeverityStyle(AlertSeverity severity)
        {
            // Dark-theme palette consistent with the shell (#1E1E1E base, #0E639C accent).
            (Color bg, Color border, Color label) = severity switch
            {
                AlertSeverity.Warning => (Color.FromRgb(0x4A, 0x3C, 0x16), Color.FromRgb(0xC8, 0xA0, 0x2C), Color.FromRgb(0xF0, 0xD2, 0x6A)),
                AlertSeverity.Error   => (Color.FromRgb(0x4A, 0x1E, 0x1E), Color.FromRgb(0xC0, 0x40, 0x40), Color.FromRgb(0xF0, 0x9A, 0x9A)),
                _                     => (Color.FromRgb(0x16, 0x32, 0x4A), Color.FromRgb(0x0E, 0x63, 0x9C), Color.FromRgb(0x8A, 0xC8, 0xF0)),
            };

            BannerBorder.Background = new SolidColorBrush(bg);
            BannerBorder.BorderBrush = new SolidColorBrush(border);
            SeverityLabel.Foreground = new SolidColorBrush(label);
        }
    }
}
