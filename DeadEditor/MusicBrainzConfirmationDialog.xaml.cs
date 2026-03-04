using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeadEditor.Models;

namespace DeadEditor
{
    public partial class MusicBrainzConfirmationDialog : Window
    {
        public MusicBrainzConfirmationDialog(ReleaseOption selectedRelease, AlbumType albumType, int trackCount)
        {
            InitializeComponent();

            // Populate release information
            TitleTextBlock.Text = selectedRelease.Title;
            ArtistTextBlock.Text = selectedRelease.Artist;
            YearTextBlock.Text = selectedRelease.Year ?? "Unknown";
            LabelTextBlock.Text = selectedRelease.Label ?? "Unknown";
            CountryTextBlock.Text = selectedRelease.Country ?? "Unknown";
            FormatTextBlock.Text = selectedRelease.Format ?? "Unknown";

            // Build field updates list based on album type
            BuildFieldUpdatesList(selectedRelease, albumType);

            // Display track information
            var mbTrackCount = selectedRelease.Tracks?.Count ?? 0;
            if (mbTrackCount > 0)
            {
                TrackInfoTextBlock.Text = $"{mbTrackCount} MusicBrainz tracks will populate the Preview Metadata column";
            }
            else
            {
                TrackInfoTextBlock.Text = "No track data available from MusicBrainz";
            }
        }

        private void BuildFieldUpdatesList(ReleaseOption selectedRelease, AlbumType albumType)
        {
            FieldUpdatesPanel.Children.Clear();

            // Helper method to add a field update item
            void AddFieldUpdate(string fieldName, string value, bool isUnchanged = false)
            {
                var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

                var checkmark = new TextBlock
                {
                    Text = isUnchanged ? "ℹ️" : "✓",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 10, 0),
                    Foreground = isUnchanged ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.LightGreen
                };

                var label = new TextBlock
                {
                    Text = $"{fieldName} →",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 10, 0),
                    Foreground = isUnchanged ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.White
                };

                var valueText = new TextBlock
                {
                    Text = $"\"{value}\"",
                    Foreground = isUnchanged ? System.Windows.Media.Brushes.Gray : new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap
                };

                panel.Children.Add(checkmark);
                panel.Children.Add(label);
                panel.Children.Add(valueText);

                FieldUpdatesPanel.Children.Add(panel);
            }

            // Album-type-specific fields
            switch (albumType)
            {
                case AlbumType.Studio:
                    AddFieldUpdate("Album Name", selectedRelease.Title);
                    AddFieldUpdate("Artist", selectedRelease.Artist);
                    AddFieldUpdate("Release Year", selectedRelease.Year ?? "Unknown");
                    if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                        AddFieldUpdate("Artwork", "Downloaded from Cover Art Archive");
                    if (selectedRelease.Tracks != null && selectedRelease.Tracks.Count > 0)
                        AddFieldUpdate("Track Titles", $"{selectedRelease.Tracks.Count} tracks");
                    break;

                case AlbumType.BoxSet:
                    AddFieldUpdate("Box Set Name", selectedRelease.Title);
                    AddFieldUpdate("Artist", selectedRelease.Artist);
                    AddFieldUpdate("Release Year", selectedRelease.Year ?? "Unknown");
                    if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                        AddFieldUpdate("Artwork", "Downloaded from Cover Art Archive");
                    if (selectedRelease.Tracks != null && selectedRelease.Tracks.Count > 0)
                        AddFieldUpdate("Track Titles", $"{selectedRelease.Tracks.Count} tracks");
                    break;

                case AlbumType.OfficialRelease:
                    AddFieldUpdate("Official Release", selectedRelease.Title);
                    AddFieldUpdate("Artist", selectedRelease.Artist);
                    AddFieldUpdate("Release Year", selectedRelease.Year ?? "Unknown");
                    if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                        AddFieldUpdate("Artwork", "Downloaded from Cover Art Archive");
                    if (selectedRelease.Tracks != null && selectedRelease.Tracks.Count > 0)
                        AddFieldUpdate("Track Titles", $"{selectedRelease.Tracks.Count} tracks");
                    break;

                case AlbumType.Live:
                    AddFieldUpdate("Artist", selectedRelease.Artist);
                    AddFieldUpdate("Release Year", selectedRelease.Year ?? "Unknown");
                    if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                        AddFieldUpdate("Artwork", "Downloaded from Cover Art Archive");
                    if (selectedRelease.Tracks != null && selectedRelease.Tracks.Count > 0)
                        AddFieldUpdate("Track Titles", $"{selectedRelease.Tracks.Count} tracks");

                    // Add note about unchanged fields
                    var separator = new Separator { Margin = new Thickness(0, 10, 0, 10) };
                    FieldUpdatesPanel.Children.Add(separator);

                    var noteText = new TextBlock
                    {
                        Text = "Date, Venue, City, and State will remain unchanged (you control these fields)",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180)),
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    FieldUpdatesPanel.Children.Add(noteText);
                    break;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
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
