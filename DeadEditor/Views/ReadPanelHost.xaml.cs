using System.Windows;
using DeadEditor.Services;

namespace DeadEditor.Views
{
    /// <summary>
    /// The shell's in-window read panel (alert-system-spec.md Ruling 3) for viewing a long text
    /// document — the imported info <c>.txt</c> file (#33). Implements <see cref="IReadPanelHost"/>;
    /// the <see cref="AlertService"/> forwards here on the UI thread. A full-shell dimmed scrim + a
    /// LARGE centered card with a scrollable, read-only, selectable monospace text area and a Close
    /// button. Silent by construction (no MessageBeep / SystemSounds — the ConfirmHost precedent).
    /// <para>
    /// It is a VIEWER, not a decision: no result, no buttons-as-choices, so it shares the scrim/card
    /// mold but none of ConfirmHost's <c>TaskCompletionSource</c>/result/default-button machinery.
    /// Keyboard: the shell routes Esc to <see cref="Hide"/> while <see cref="IsShowing"/> is true and
    /// — unlike the confirm gate — lets all other keys reach the focused text area so PgUp/PgDn/arrows
    /// scroll; mouse-wheel scrolls natively over the text. Re-entrancy is not guarded: the panel holds
    /// no pending state and the scrim blocks a second UI-initiated open, so a second <see cref="Show"/>
    /// simply replaces the displayed text.
    /// </para>
    /// </summary>
    public partial class ReadPanelHost : System.Windows.Controls.UserControl, IReadPanelHost
    {
        public ReadPanelHost()
        {
            InitializeComponent();
        }

        /// <summary>True while the read panel is on screen (the scrim is up).</summary>
        public bool IsShowing => Visibility == Visibility.Visible;

        /// <summary>IReadPanelHost — always invoked on the UI thread (the service forwards directly).</summary>
        public void Show(string title, string content)
        {
            TitleText.Text = title;
            ContentText.Text = content;

            // Reset the scroll/caret to the top so each open starts at the document head.
            ContentText.CaretIndex = 0;
            ContentText.ScrollToHome();

            Visibility = Visibility.Visible;

            // Focus the text area so PgUp/PgDn/arrow keys scroll it immediately. Defer until after
            // layout so the freshly-shown control is focusable.
            Dispatcher.BeginInvoke(new System.Action(() => ContentText.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>Dismiss the panel (Close button or Esc, routed by the shell).</summary>
        public void Hide() => Visibility = Visibility.Collapsed;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
    }
}
