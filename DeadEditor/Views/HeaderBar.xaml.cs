using DeadEditor.Models;
using System.Windows;

namespace DeadEditor
{
    public partial class HeaderBar : System.Windows.Controls.UserControl
    {
        private AlbumDetailView? _currentAlbumView;

        public HeaderBar()
        {
            InitializeComponent();
        }

        public void ShowLibraryHeader(LibraryGridView libraryView)
        {
            LibraryHeader.Visibility = Visibility.Visible;
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
            PlaceholderHeader.Visibility = Visibility.Collapsed;

            // Update concert count
            ConcertCountText.Text = libraryView.ConcertCount == 1 ? "1 concert" : $"{libraryView.ConcertCount} concerts";
        }

        public void ShowAlbumDetailHeader(AlbumDetailView albumView, object? context)
        {
            _currentAlbumView = albumView;

            LibraryHeader.Visibility = Visibility.Collapsed;
            AlbumDetailHeader.Visibility = Visibility.Visible;
            PlaceholderHeader.Visibility = Visibility.Collapsed;

            // Update album name and type
            AlbumNameText.Text = albumView.AlbumName;

            if (context is LibraryShow show)
            {
                AlbumTypeText.Text = show.Type == AlbumType.OfficialRelease ? "Official Release" : "Audience Recording";
            }
        }

        public void ShowImportHeader()
        {
            LibraryHeader.Visibility = Visibility.Collapsed;
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
            PlaceholderHeader.Visibility = Visibility.Visible;
            PlaceholderText.Text = "Import";
        }

        public void ShowSettingsHeader()
        {
            LibraryHeader.Visibility = Visibility.Collapsed;
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
            PlaceholderHeader.Visibility = Visibility.Visible;
            PlaceholderText.Text = "Settings";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentAlbumView?.NavigateBack();
        }
    }
}
