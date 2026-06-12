using System;
using System.Threading.Tasks;
using System.Windows;
using DeadEditor.Services;

namespace DeadEditor.Views
{
    /// <summary>
    /// The shell's in-window confirm host (alert-system-spec.md Ruling 2). Implements
    /// <see cref="IConfirmHost"/>; the <see cref="AlertService"/> forwards blocking decisions here on
    /// the UI thread. Renders a full-shell dimmed scrim + centered card, resolves the await via a
    /// <see cref="TaskCompletionSource{TResult}"/>, and is silent by construction (no MessageBeep /
    /// SystemSounds — the PullCollisionDialog precedent generalized into a reusable overlay).
    /// <para>
    /// Keyboard is gated by the shell: while <see cref="IsShowing"/> is true,
    /// <c>ShellWindow_PreviewKeyDown</c> swallows every key (so no shell shortcut leaks behind the
    /// scrim) and routes Enter to <see cref="ConfirmByKeyboard"/> and Esc to
    /// <see cref="CancelByKeyboard"/>. The card still takes focus (the Confirm button is focused on
    /// show) so it reads as the active surface — the deliberate contrast with the never-focusing
    /// banner.
    /// </para>
    /// </summary>
    public partial class ConfirmHost : System.Windows.Controls.UserControl, IConfirmHost
    {
        private TaskCompletionSource<bool>? _pending;

        public ConfirmHost()
        {
            InitializeComponent();
        }

        /// <summary>True while a confirm card is on screen (the scrim is up).</summary>
        public bool IsShowing => _pending != null;

        /// <summary>IConfirmHost — always invoked on the UI thread (the service forwards directly).</summary>
        public Task<bool> ConfirmAsync(string message, string title, string confirmLabel, string cancelLabel)
        {
            // Re-entrancy: one blocking decision owns the surface at a time. The scrim structurally
            // prevents a second user-initiated confirm, so a second call is a logic error — refuse
            // loudly rather than silently queue a prompt the user never expected to be deferred.
            if (_pending != null)
                throw new InvalidOperationException(
                    "A confirm is already showing; only one blocking decision may be active at a time.");

            _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmLabel;
            CancelButton.Content = cancelLabel;

            Visibility = Visibility.Visible;

            // The card takes keyboard focus BY DESIGN (contrast with the banner, which never focuses):
            // a blocking decision is the one surface that should own focus. Defer the Focus() until
            // after layout so the freshly-shown button is focusable.
            Dispatcher.BeginInvoke(new Action(() => ConfirmButton.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);

            return _pending.Task;
        }

        /// <summary>Enter / the affirmative button: resolve true.</summary>
        public void ConfirmByKeyboard() => Resolve(true);

        /// <summary>Esc / the negative button: resolve false (the safe answer).</summary>
        public void CancelByKeyboard() => Resolve(false);

        private void ConfirmButton_Click(object sender, RoutedEventArgs e) => Resolve(true);

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Resolve(false);

        private void Resolve(bool result)
        {
            var tcs = _pending;
            if (tcs == null) return;

            // Tear down the surface first, then complete — so a continuation that immediately raises
            // another confirm sees IsShowing == false and is allowed.
            _pending = null;
            Visibility = Visibility.Collapsed;
            tcs.SetResult(result);
        }
    }
}
