using System;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DeadEditor
{
    /// <summary>
    /// Host-agnostic Match Setlist review surface (decision (a)). A plain
    /// <see cref="UserControl"/> — no <c>DialogResult</c>, no App.Alerts
    /// coupling. The host sets a <see cref="MatchReviewViewModel"/> as its
    /// DataContext (or passes one to the ctor), subscribes to
    /// <see cref="Confirmed"/> / <see cref="Cancelled"/>, and on Confirm reads
    /// <see cref="MatchReviewViewModel.BuildEditedProposalSet"/> to hand to
    /// <c>SetlistMatcher.Apply</c>. Nothing wires this up yet (B2b commit 1 is
    /// an inert scaffold).
    /// </summary>
    public partial class MatchReviewDialog : WpfUserControl
    {
        /// <summary>Raised when the user accepts the resolved changes (Apply).</summary>
        public event EventHandler? Confirmed;

        /// <summary>Raised when the user dismisses the review without applying.</summary>
        public event EventHandler? Cancelled;

        public MatchReviewDialog()
        {
            InitializeComponent();
        }

        public MatchReviewDialog(MatchReviewViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        /// <summary>The bound container VM, if one has been set.</summary>
        public MatchReviewViewModel? ViewModel => DataContext as MatchReviewViewModel;

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
            => Confirmed?.Invoke(this, EventArgs.Empty);

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
