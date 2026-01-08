using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DeadEditor
{
    public partial class AddSongDialog : Window
    {
        private readonly NormalizationService _normalizationService;

        public AddSongDialog(NormalizationService normalizationService)
        {
            InitializeComponent();
            _normalizationService = normalizationService;
            LoadArtists();

            // Set default artist to Grateful Dead
            ArtistComboBox.Text = "Grateful Dead";
        }

        private void LoadArtists()
        {
            var artists = _normalizationService.GetAllArtists();

            // Add common artists if not present
            var commonArtists = new List<string> { "Grateful Dead", "New Riders of the Purple Sage", "Jerry Garcia Band", "Bob Weir", "Phil Lesh" };
            foreach (var artist in commonArtists)
            {
                if (!artists.Contains(artist))
                {
                    artists.Add(artist);
                }
            }

            artists = artists.OrderBy(a => a).ToList();

            ArtistComboBox.ItemsSource = artists;
        }

        private void RefreshArtistsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadArtists();
            StatusTextBlock.Text = "Artist list refreshed";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            var artistName = ArtistComboBox.Text.Trim();
            var officialTitle = OfficialTitleTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(artistName))
            {
                StatusTextBlock.Text = "Please enter an artist name";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                ArtistComboBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(officialTitle))
            {
                StatusTextBlock.Text = "Please enter a song title";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                OfficialTitleTextBox.Focus();
                return;
            }

            // Parse aliases (one per line)
            var aliasesText = AliasesTextBox.Text.Trim();
            var aliases = new List<string>();

            if (!string.IsNullOrWhiteSpace(aliasesText))
            {
                aliases = aliasesText
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
            }

            // Add song
            try
            {
                _normalizationService.AddSong(officialTitle, aliases, artistName);

                StatusTextBlock.Text = $"✓ Song '{officialTitle}' added successfully!";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;

                // Clear form for next entry
                OfficialTitleTextBox.Clear();
                AliasesTextBox.Clear();
                OfficialTitleTextBox.Focus();

                // Optionally close dialog after successful add
                // DialogResult = true;
                // Close();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
