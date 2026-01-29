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
                System.Windows.MessageBox.Show("Please select a release.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
    }
}
