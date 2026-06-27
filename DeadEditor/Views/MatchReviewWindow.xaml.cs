using System.Windows;

namespace DeadEditor
{
    /// <summary>
    /// Thin modal <see cref="Window"/> hosting the host-agnostic
    /// <see cref="MatchReviewDialog"/> UserControl. The Window owns the
    /// <c>DialogResult</c> the synchronous helper reads after
    /// <see cref="Window.ShowDialog"/>: the control's <c>Confirmed</c> event
    /// maps to <c>true</c> (Apply), <c>Cancelled</c> to <c>false</c>. Setting
    /// <c>DialogResult</c> closes the modal.
    ///
    /// Dismissing the window (close box / Esc) leaves <c>DialogResult</c> null,
    /// which the helper treats the same as Cancel.
    /// </summary>
    public partial class MatchReviewWindow : Window
    {
        public MatchReviewWindow(MatchReviewViewModel viewModel)
        {
            InitializeComponent();
            ReviewDialog.DataContext = viewModel;
            ReviewDialog.Confirmed += (_, __) => DialogResult = true;
            ReviewDialog.Cancelled += (_, __) => DialogResult = false;
        }
    }
}
