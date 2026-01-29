using System.Windows;

namespace DeadEditor
{
    public partial class AlbumSearchDialog : Window
    {
        public string AlbumName { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string? Year { get; private set; }

        public AlbumSearchDialog(string initialAlbumName = "", string initialArtist = "")
        {
            InitializeComponent();

            AlbumNameTextBox.Text = initialAlbumName;
            ArtistTextBox.Text = initialArtist;

            // Focus on first empty field
            if (string.IsNullOrWhiteSpace(initialAlbumName))
            {
                AlbumNameTextBox.Focus();
            }
            else if (string.IsNullOrWhiteSpace(initialArtist))
            {
                ArtistTextBox.Focus();
            }
            else
            {
                YearTextBox.Focus();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            var albumName = AlbumNameTextBox.Text.Trim();
            var artist = ArtistTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(albumName) || string.IsNullOrWhiteSpace(artist))
            {
                System.Windows.MessageBox.Show("Please enter both Album Name and Artist.", "Missing Information",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AlbumName = albumName;
            Artist = artist;
            Year = string.IsNullOrWhiteSpace(YearTextBox.Text) ? null : YearTextBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
