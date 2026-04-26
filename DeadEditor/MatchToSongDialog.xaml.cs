using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeadEditor
{
    /// <summary>
    /// Dialog that lets the user match an unmatched track to a setlist song.
    /// Shows unmatched setlist songs with their set/position labels.
    /// </summary>
    public partial class MatchToSongDialog : Window
    {
        /// <summary>
        /// The index into the flattened setlist that the user selected, or -1 if cancelled.
        /// </summary>
        public int SelectedSetlistIndex { get; private set; } = -1;

        private readonly List<int> _setlistIndices;

        /// <param name="trackTitle">The current track title to display.</param>
        /// <param name="unmatchedSongs">List of (setlistIndex, displayLabel) for songs not yet matched.</param>
        public MatchToSongDialog(string trackTitle, List<(int SetlistIndex, string DisplayLabel)> unmatchedSongs)
        {
            InitializeComponent();

            CurrentTitleText.Text = trackTitle;
            _setlistIndices = new List<int>();

            foreach (var (index, label) in unmatchedSongs)
            {
                SetlistSongsListBox.Items.Add(label);
                _setlistIndices.Add(index);
            }
        }

        private void SetlistSongsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MatchButton.IsEnabled = SetlistSongsListBox.SelectedIndex >= 0;
        }

        private void MatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SetlistSongsListBox.SelectedIndex >= 0)
            {
                SelectedSetlistIndex = _setlistIndices[SetlistSongsListBox.SelectedIndex];
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
