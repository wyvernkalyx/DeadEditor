using System.Collections.Generic;
using System.Windows;

namespace DeadEditor
{
    public partial class ReleaseSelectorDialog : Window
    {
        public ReleaseOption? SelectedRelease { get; private set; }

        public ReleaseSelectorDialog(string albumInfo, List<ReleaseOption> releases)
        {
            InitializeComponent();

            AlbumInfoTextBlock.Text = albumInfo;
            ReleasesDataGrid.ItemsSource = releases;

            // Select first item by default
            if (releases.Count > 0)
            {
                ReleasesDataGrid.SelectedIndex = 0;
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
            {
                SelectedRelease = selected;
                DialogResult = true;
                Close();
            }
            else
            {
                // Inline validation (Ruling 6: #12) — adjacent to the Select button, no MessageBox.
                ValidationText.Text = "Select a release from the list.";
                ValidationText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Clear the #12 inline validation once a row is selected (Ruling 6: "clears when the
        /// user corrects the input").</summary>
        private void ReleasesDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ValidationText != null && ReleasesDataGrid.SelectedItem != null)
                ValidationText.Visibility = Visibility.Collapsed;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ReleasesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
            {
                SelectedRelease = selected;
                DialogResult = true;
                Close();
            }
        }
    }

    public class ReleaseOption
    {
        public string Title { get; set; } = "";
        public string? Year { get; set; }
        public string? Label { get; set; }
        public string? Country { get; set; }
        public string? Format { get; set; }
        public string ReleaseId { get; set; } = "";
        public string? ArtworkUrl { get; set; }
        public string Artist { get; set; } = "";
        public int? TotalTrackCount { get; set; }  // Total tracks across all media/discs
        public List<MusicBrainzTrack>? Tracks { get; set; }  // Track listings from MusicBrainz (optional)
    }

    public class MusicBrainzTrack
    {
        public int DiscNumber { get; set; }      // Disc number (1-based)
        public int Position { get; set; }        // Track number within disc (1-based)
        public string Title { get; set; } = "";  // Track title from MusicBrainz
        public int? Length { get; set; }         // Duration in milliseconds (optional)
    }
}
