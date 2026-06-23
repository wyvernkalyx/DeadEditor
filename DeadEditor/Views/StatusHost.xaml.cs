using System.Windows;
using System.Windows.Media.Animation;
using DeadEditor.Services;

namespace DeadEditor.Views
{
    /// <summary>
    /// The shell's in-window status overlay (alert-system-spec.md § Status overlay) — the modal
    /// "please wait" surface for long/blocking operations. Implements <see cref="IStatusHost"/>; the
    /// <see cref="AlertService"/> drives it (always on the UI thread) from
    /// <see cref="AlertService.RunWithStatusAsync"/>. A full-shell dimmed scrim + centered card with
    /// an indeterminate spinner, a title, and an optional message line. Silent by construction (no
    /// MessageBeep / SystemSounds — the ConfirmHost/ReadPanelHost precedent).
    /// <para>
    /// NON-CANCELABLE by design: no buttons, no Esc handling here (the shell swallows every key while
    /// <see cref="IsShowing"/> is true). It is the one host with no result and no decision — pure
    /// please-wait chrome. The spinner storyboard runs only while shown (Begin on
    /// <see cref="ShowStatus"/>, Stop on <see cref="HideStatus"/>).
    /// </para>
    /// </summary>
    public partial class StatusHost : System.Windows.Controls.UserControl, IStatusHost
    {
        public StatusHost()
        {
            InitializeComponent();
        }

        /// <summary>True while the status overlay is on screen (the scrim is up).</summary>
        public bool IsShowing => Visibility == Visibility.Visible;

        /// <summary>
        /// IStatusHost — always invoked on the UI thread (the service marshals). Sets the title and
        /// (optional) message, shows the scrim, and starts the spinner. Last-write-wins: a re-entered
        /// scope simply overwrites the displayed title/message.
        /// </summary>
        public void ShowStatus(string title, string? message)
        {
            TitleText.Text = title;
            SetMessage(message);

            Visibility = Visibility.Visible;
            SpinnerStoryboard().Begin(this, true);
        }

        /// <summary>Update the message line mid-operation; reveals it if it was collapsed.</summary>
        public void UpdateStatus(string message) => SetMessage(message);

        /// <summary>Collapse the whole overlay and stop the spinner.</summary>
        public void HideStatus()
        {
            SpinnerStoryboard().Stop(this);
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Show the message line when non-empty; collapse it when null/empty.</summary>
        private void SetMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                MessageText.Text = string.Empty;
                MessageText.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageText.Text = message;
                MessageText.Visibility = Visibility.Visible;
            }
        }

        private Storyboard SpinnerStoryboard() => (Storyboard)Resources["SpinnerStoryboard"];
    }
}
