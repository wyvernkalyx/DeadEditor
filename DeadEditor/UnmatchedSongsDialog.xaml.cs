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

        // Ruling 6 (#13/#14): the dialog no longer shows its own MessageBox — it is a terminal dialog
        // that closes immediately, so it returns the outcome here and each caller fires
        // App.Alerts.Notify on the shell banner AFTER ShowDialog returns. SongsAddedCount drives the
        // #13 Info ("Added N songs"); ApplyErrorMessage (non-null) drives the #14 Error.
        public int SongsAddedCount { get; private set; }
        public string? ApplyErrorMessage { get; private set; }

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

                // Ruling 6: hand the outcome to the caller instead of showing an in-dialog MessageBox.
                SongsAddedCount = songsAdded.Count;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                // Terminal error: record it for the caller's shell-banner notify (#14), then close so
                // ShowDialog returns. DialogResult=true because corrections applied before the throw
                // are already live (ChangesMade reflects that) and the caller should process them.
                ApplyErrorMessage = ex.Message;
                DialogResult = true;
                Close();
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
