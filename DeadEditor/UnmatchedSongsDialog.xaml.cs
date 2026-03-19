using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DeadEditor.Models;
using DeadEditor.Services;

namespace DeadEditor
{
    public partial class UnmatchedSongsDialog : Window
    {
        private readonly List<TrackInfo> _unmatchedTracks;
        private readonly NormalizationService _normalizationService;
        private readonly List<System.Windows.Controls.ComboBox> _correctionComboBoxes = new List<System.Windows.Controls.ComboBox>();
        private readonly List<string> _allSongTitles;

        public bool ChangesMade { get; private set; } = false;

        public UnmatchedSongsDialog(List<TrackInfo> unmatchedTracks, NormalizationService normalizationService)
        {
            InitializeComponent();

            _unmatchedTracks = unmatchedTracks;
            _normalizationService = normalizationService;
            _allSongTitles = _normalizationService.GetAllTitles();

            BuildUI();
        }

        private void BuildUI()
        {
            foreach (var track in _unmatchedTracks)
            {
                // Create a row for each unmatched track
                var rowPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                // Track info label (track # and raw title)
                var infoLabel = new System.Windows.Controls.TextBlock
                {
                    Text = $"Track {track.TrackNumber}: {track.RawTitle ?? track.SongName}",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                rowPanel.Children.Add(infoLabel);

                // ComboBox for correction (editable, with autocomplete)
                var comboBox = new System.Windows.Controls.ComboBox
                {
                    IsEditable = true,
                    IsTextSearchEnabled = true,
                    ItemsSource = _allSongTitles,
                    Text = track.SongName, // Pre-fill with current song name
                    Width = 500,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Tag = track // Store reference to track
                };

                _correctionComboBoxes.Add(comboBox);
                rowPanel.Children.Add(comboBox);

                // Separator line
                var separator = new System.Windows.Controls.Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                rowPanel.Children.Add(separator);

                UnmatchedTracksPanel.Children.Add(rowPanel);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var songsAdded = new List<string>();

                // Process each correction
                foreach (var comboBox in _correctionComboBoxes)
                {
                    var track = comboBox.Tag as TrackInfo;
                    var correctedName = comboBox.Text?.Trim();

                    if (track == null || string.IsNullOrEmpty(correctedName))
                        continue;

                    // Only apply if user actually changed the value
                    if (correctedName != track.SongName)
                    {
                        // Update track
                        track.SongName = correctedName;
                        track.IsMatched = true; // Mark as matched now

                        // If this song is not in the database, add it
                        if (!_allSongTitles.Contains(correctedName, StringComparer.OrdinalIgnoreCase))
                        {
                            _normalizationService.AddSong(correctedName, null);
                            songsAdded.Add(correctedName);
                        }

                        ChangesMade = true;
                    }
                }

                // Show summary if songs were added
                if (songsAdded.Count > 0)
                {
                    var message = $"Added {songsAdded.Count} new song(s) to songs.json:\n" +
                                  string.Join("\n", songsAdded.Select(s => $"  • {s}"));
                    System.Windows.MessageBox.Show(message, "Songs Added", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error applying corrections: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
