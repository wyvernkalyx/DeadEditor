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
    /// Two-way and three-way share ALL plumbing: one <see cref="ConfirmResult"/>-typed completion,
    /// one scrim/card, one re-entrancy guard. The two-way path hides the middle button and maps the
    /// tri-state result down to a bool (<see cref="ConfirmResult.Confirm"/> → true, else false). The
    /// three-way path shows the middle (Decline) button.
    /// </para>
    /// <para>
    /// Keyboard is gated by the shell: while <see cref="IsShowing"/> is true,
    /// <c>ShellWindow_PreviewKeyDown</c> swallows every key (so no shell shortcut leaks behind the
    /// scrim) and routes Enter to <see cref="ConfirmByKeyboard"/> (the default/focused button) and Esc
    /// to <see cref="CancelByKeyboard"/> (always the safe answer — false / Cancel). The card takes
    /// focus on show (the default button) so it reads as the active surface — the deliberate contrast
    /// with the never-focusing banner.
    /// </para>
    /// </summary>
    public partial class ConfirmHost : System.Windows.Controls.UserControl, IConfirmHost
    {
        private TaskCompletionSource<ConfirmResult>? _pending;

        // Which result the default/focused button (Enter) produces for the current card. Esc is
        // always Cancel and does not depend on this.
        private ConfirmResult _enterResult = ConfirmResult.Confirm;

        public ConfirmHost()
        {
            InitializeComponent();
        }

        /// <summary>True while a confirm card is on screen (the scrim is up).</summary>
        public bool IsShowing => _pending != null;

        /// <summary>IConfirmHost two-way — always invoked on the UI thread (the service forwards directly).</summary>
        public async Task<bool> ConfirmAsync(string message, string title, string confirmLabel, string cancelLabel,
                                             bool defaultToConfirm = true)
        {
            // Two-way: no Decline button. The default/focused button is Confirm unless the caller asks
            // for the safe answer to be default (defaultToConfirm == false), preserving a site that
            // deliberately defaulted to No. Esc -> Cancel -> false.
            var enter = defaultToConfirm ? ConfirmResult.Confirm : ConfirmResult.Cancel;
            var result = await Show(message, title, confirmLabel, declineLabel: null, cancelLabel, enter);
            return result == ConfirmResult.Confirm;
        }

        /// <summary>IConfirmHost three-way — always invoked on the UI thread.</summary>
        public Task<ConfirmResult> ConfirmAsync(string message, string title,
                                                string confirmLabel, string declineLabel, string cancelLabel)
        {
            // Three-way: default/focused button is Confirm; Esc -> Cancel (the tri-state safe answer).
            return Show(message, title, confirmLabel, declineLabel, cancelLabel, ConfirmResult.Confirm);
        }

        private Task<ConfirmResult> Show(string message, string title,
                                         string confirmLabel, string? declineLabel, string cancelLabel,
                                         ConfirmResult enterResult)
        {
            // Re-entrancy: one blocking decision owns the surface at a time. The scrim structurally
            // prevents a second user-initiated confirm, so a second call is a logic error — refuse
            // loudly rather than silently queue a prompt the user never expected to be deferred.
            if (_pending != null)
                throw new InvalidOperationException(
                    "A confirm is already showing; only one blocking decision may be active at a time.");

            _pending = new TaskCompletionSource<ConfirmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _enterResult = enterResult;

            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmLabel;
            CancelButton.Content = cancelLabel;

            if (declineLabel != null)
            {
                DeclineButton.Content = declineLabel;
                DeclineButton.Visibility = Visibility.Visible;
            }
            else
            {
                DeclineButton.Visibility = Visibility.Collapsed;
            }

            Visibility = Visibility.Visible;

            // Single source of truth for "the default button": both the accent fill AND keyboard focus
            // derive from this one reference, so the highlighted button is always the Enter target
            // (gate finding 2026-06-12 — they used to diverge: accent hardcoded to Confirm, focus on
            // the safe button when defaultToConfirm:false).
            var defaultButton = enterResult switch
            {
                ConfirmResult.Decline => (System.Windows.Controls.Button)DeclineButton,
                ConfirmResult.Cancel => CancelButton,
                _ => ConfirmButton
            };

            // Accent the default button; the others drop to the plain secondary style.
            ApplyDefaultButtonStyling(defaultButton);

            // The card takes keyboard focus BY DESIGN (contrast with the banner, which never focuses).
            // Defer until after layout so the freshly-shown button is focusable.
            Dispatcher.BeginInvoke(new Action(() => defaultButton.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);

            return _pending.Task;
        }

        /// <summary>
        /// Apply the accent (PrimaryButton) fill to the default button and the plain style to the
        /// other two. Driven by the same <c>defaultButton</c> reference that takes focus, so the
        /// visual highlight and the Enter target can never disagree.
        /// </summary>
        private void ApplyDefaultButtonStyling(System.Windows.Controls.Button defaultButton)
        {
            var accent = (Style)Resources["PrimaryButton"];
            var plain = (Style)Resources[typeof(System.Windows.Controls.Button)];

            foreach (var button in new[] { ConfirmButton, DeclineButton, CancelButton })
                button.Style = ReferenceEquals(button, defaultButton) ? accent : plain;
        }

        /// <summary>Enter: resolve the default/focused button's result.</summary>
        public void ConfirmByKeyboard() => Resolve(_enterResult);

        /// <summary>Esc: resolve Cancel — the safe answer (two-way maps to false).</summary>
        public void CancelByKeyboard() => Resolve(ConfirmResult.Cancel);

        private void ConfirmButton_Click(object sender, RoutedEventArgs e) => Resolve(ConfirmResult.Confirm);

        private void DeclineButton_Click(object sender, RoutedEventArgs e) => Resolve(ConfirmResult.Decline);

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Resolve(ConfirmResult.Cancel);

        private void Resolve(ConfirmResult result)
        {
            var tcs = _pending;
            if (tcs == null) return;

            // Tear down the surface first, then complete — so a continuation that immediately raises
            // another confirm sees IsShowing == false and is allowed. Reset the Decline button so the
            // next two-way card is visually unchanged.
            _pending = null;
            Visibility = Visibility.Collapsed;
            DeclineButton.Visibility = Visibility.Collapsed;
            tcs.SetResult(result);
        }
    }
}
