using System.Windows;
using DeadEditor.Helpers;

namespace DeadEditor
{
    /// <summary>
    /// Modal Replace / Append / Cancel prompt shown when "pull setlist for date" targets a
    /// date that already has tracks in the box. Runs synchronously on the UI thread via
    /// <c>ShowDialog()</c>; the caller reads <see cref="Result"/> (it does not rely on
    /// <c>DialogResult</c>, so a window-close / Esc maps to <see cref="PullCollisionAction.Cancel"/>
    /// like the explicit Cancel button).
    /// </summary>
    public partial class PullCollisionDialog : Window
    {
        /// <summary>The curator's choice. Defaults to Cancel so dismissing the window
        /// (close box or Esc) is a no-op for the pull.</summary>
        public PullCollisionAction Result { get; private set; } = PullCollisionAction.Cancel;

        /// <param name="date">The collided performance date (yyyy-MM-dd).</param>
        /// <param name="existingCount">How many rows that date already has in the box.</param>
        public PullCollisionDialog(string date, int existingCount)
        {
            InitializeComponent();

            var noun = existingCount == 1 ? "track" : "tracks";
            MessageText.Text =
                $"Date {date} already has {existingCount} {noun} in this box.\n\n" +
                "Replace them with the pulled setlist, or append the pulled tracks alongside?";
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            Result = PullCollisionAction.Replace;
            DialogResult = true;
        }

        private void AppendButton_Click(object sender, RoutedEventArgs e)
        {
            Result = PullCollisionAction.Append;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = PullCollisionAction.Cancel;
            DialogResult = false;
        }
    }
}
