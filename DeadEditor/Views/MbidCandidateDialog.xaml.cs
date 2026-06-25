using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;

namespace DeadEditor.Views
{
    public class MbidApplyResult
    {
        public string Mbid { get; set; } = "";
        public bool ApplyAlbumTitle { get; set; }
        public bool ApplyAlbumArtist { get; set; }
        public bool ApplyReleaseYear { get; set; }
        public bool ApplyTrackTitles { get; set; }
        public ReleaseOption? SelectedRelease { get; set; }
    }

    // Relocated here from the deleted MbidMigrationService (MusicBrainz-removal arc) so the
    // dialog keeps building; both this enum and the dialog are removed together in commit 6.
    public enum CandidateAction
    {
        None,
        Confirm,
        Skip
    }

    public partial class MbidCandidateDialog : Window
    {
        private readonly List<ReleaseOption> _candidates;
        private readonly AlbumInfo? _currentAlbumInfo;
        private readonly bool _showFieldCheckboxes;
        private RadioButton? _selectedRadio;
        private string? _selectedMbid;
        private ReleaseOption? _selectedCandidate;

        public CandidateAction UserAction { get; private set; } = CandidateAction.None;
        public string? SelectedMbid { get; private set; }
        public MbidApplyResult? ApplyResult { get; private set; }

        public MbidCandidateDialog(
            LibraryShow album,
            List<ReleaseOption> candidates,
            string? dryRunPreSelectedMbid,
            string? warning,
            AlbumInfo? currentAlbumInfo = null,
            bool showFieldCheckboxes = false)
        {
            InitializeComponent();
            _candidates = candidates;
            _currentAlbumInfo = currentAlbumInfo;
            _showFieldCheckboxes = showFieldCheckboxes;

            // Album info panel
            var folderName = System.IO.Path.GetFileName(album.FolderPaths.FirstOrDefault() ?? "");
            var albumName = !string.IsNullOrEmpty(album.AlbumName) ? album.AlbumName
                : !string.IsNullOrEmpty(album.OfficialRelease) ? album.OfficialRelease : folderName;
            var infoLines = new List<string>();
            infoLines.Add($"Album: {albumName}");
            if (!string.IsNullOrEmpty(album.Date)) infoLines.Add($"Date: {album.Date}");
            infoLines.Add($"Tracks: {album.TrackCount}");
            infoLines.Add($"Folder: {folderName}");
            AlbumInfoText.Text = string.Join("\n", infoLines);

            if (!string.IsNullOrEmpty(warning))
            {
                WarningText.Text = warning;
                WarningText.Visibility = Visibility.Visible;
            }

            // Build candidate radio buttons
            if (candidates.Count > 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    var panel = CreateCandidatePanel(candidate, i, dryRunPreSelectedMbid);
                    CandidatesPanel.Children.Add(panel);
                }
            }
            else
            {
                var noResults = new TextBlock
                {
                    Text = "No candidates found. Enter an MBID manually or skip this album.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    FontSize = 13,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                CandidatesPanel.Children.Add(noResults);
            }
        }

        private StackPanel CreateCandidatePanel(ReleaseOption candidate, int index, string? preSelectedMbid)
        {
            var container = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

            // Radio button row
            var radioRow = new StackPanel { Orientation = Orientation.Horizontal };
            var radio = new RadioButton
            {
                GroupName = "CandidateGroup",
                Tag = candidate.ReleaseId,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            radio.Checked += CandidateRadio_Checked;

            var titleText = new TextBlock
            {
                Text = $"{candidate.Title}",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                VerticalAlignment = VerticalAlignment.Center
            };

            radioRow.Children.Add(radio);
            radioRow.Children.Add(titleText);
            container.Children.Add(radioRow);

            // Details
            var details = new List<string>();
            if (!string.IsNullOrEmpty(candidate.Artist)) details.Add($"Artist: {candidate.Artist}");
            if (!string.IsNullOrEmpty(candidate.Year)) details.Add($"Year: {candidate.Year}");
            if (!string.IsNullOrEmpty(candidate.Country)) details.Add($"Country: {candidate.Country}");
            if (!string.IsNullOrEmpty(candidate.Label)) details.Add($"Label: {candidate.Label}");
            if (!string.IsNullOrEmpty(candidate.Format)) details.Add($"Format: {candidate.Format}");
            if (candidate.TotalTrackCount.HasValue) details.Add($"Tracks: {candidate.TotalTrackCount}");

            var detailBlock = new TextBlock
            {
                Text = string.Join("  |  ", details),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                Margin = new Thickness(24, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            container.Children.Add(detailBlock);

            // MBID + link
            var mbidRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 2, 0, 0) };
            var mbidText = new TextBlock
            {
                Text = $"MBID: {candidate.ReleaseId}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77))
            };
            mbidRow.Children.Add(mbidText);

            var linkButton = new Button
            {
                Content = "View on MusicBrainz",
                FontSize = 11,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(8, 0, 0, 0),
                Tag = candidate.ReleaseId
            };
            linkButton.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is string rid)
                {
                    try { Process.Start(new ProcessStartInfo($"https://musicbrainz.org/release/{rid}") { UseShellExecute = true }); }
                    catch { }
                }
            };
            mbidRow.Children.Add(linkButton);
            container.Children.Add(mbidRow);

            // Separator
            container.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 6, 0, 0)
            });

            // Pre-select if this was the dry-run choice
            if (!string.IsNullOrEmpty(preSelectedMbid) &&
                string.Equals(candidate.ReleaseId, preSelectedMbid, StringComparison.OrdinalIgnoreCase))
            {
                radio.IsChecked = true;
            }

            return container;
        }

        private void CandidateRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string mbid)
            {
                _selectedRadio = radio;
                _selectedMbid = mbid;
                _selectedCandidate = _candidates.FirstOrDefault(c =>
                    string.Equals(c.ReleaseId, mbid, StringComparison.OrdinalIgnoreCase));
                ManualRadio.IsChecked = false;
                ConfirmButton.IsEnabled = true;
                UpdateFieldCheckboxes();
            }
        }

        private void ManualRadio_Checked(object sender, RoutedEventArgs e)
        {
            _selectedRadio = null;
            _selectedMbid = null;
            _selectedCandidate = null;
            ValidateManualMbid();
            UpdateFieldCheckboxes();
        }

        private void ManualMbidTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ManualRadio.IsChecked == true)
            {
                ValidateManualMbid();
            }
        }

        private void ValidateManualMbid()
        {
            var text = ManualMbidTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ManualValidationText.Visibility = Visibility.Collapsed;
                ConfirmButton.IsEnabled = false;
                UpdateFieldCheckboxes();
                return;
            }

            if (Guid.TryParse(text, out _))
            {
                ManualValidationText.Visibility = Visibility.Collapsed;
                _selectedMbid = text.ToLowerInvariant();
                _selectedCandidate = null;
                ConfirmButton.IsEnabled = true;
                UpdateFieldCheckboxes();
            }
            else
            {
                ManualValidationText.Text = "Invalid UUID format. Expected: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";
                ManualValidationText.Visibility = Visibility.Visible;
                ConfirmButton.IsEnabled = false;
            }
        }

        private void UpdateFieldCheckboxes()
        {
            if (!_showFieldCheckboxes)
            {
                FieldsToApplyPanel.Visibility = Visibility.Collapsed;
                return;
            }

            bool hasSelection = !string.IsNullOrEmpty(_selectedMbid);
            FieldsToApplyPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;

            if (!hasSelection || _selectedCandidate == null)
            {
                ApplyAlbumTitleCheckBox.Visibility = Visibility.Collapsed;
                ApplyAlbumArtistCheckBox.Visibility = Visibility.Collapsed;
                ApplyReleaseYearCheckBox.Visibility = Visibility.Collapsed;
                return;
            }

            var candidate = _selectedCandidate;
            var current = _currentAlbumInfo;

            // Album title
            if (!string.IsNullOrEmpty(candidate.Title))
            {
                var currentTitle = current?.AlbumName ?? "";
                ApplyAlbumTitleCheckBox.Content = $"Album title: \"{currentTitle}\" \u2192 \"{candidate.Title}\"";
                ApplyAlbumTitleCheckBox.Visibility = Visibility.Visible;
                ApplyAlbumTitleCheckBox.IsChecked = false;
            }
            else
            {
                ApplyAlbumTitleCheckBox.Visibility = Visibility.Collapsed;
            }

            // Album artist
            if (!string.IsNullOrEmpty(candidate.Artist))
            {
                var currentArtist = current?.Artist ?? "";
                ApplyAlbumArtistCheckBox.Content = $"Album artist: \"{currentArtist}\" \u2192 \"{candidate.Artist}\"";
                ApplyAlbumArtistCheckBox.Visibility = Visibility.Visible;
                ApplyAlbumArtistCheckBox.IsChecked = false;
            }
            else
            {
                ApplyAlbumArtistCheckBox.Visibility = Visibility.Collapsed;
            }

            // Release year
            if (!string.IsNullOrEmpty(candidate.Year))
            {
                var currentYear = current?.Year ?? "";
                ApplyReleaseYearCheckBox.Content = $"Release year: \"{currentYear}\" \u2192 \"{candidate.Year}\"";
                ApplyReleaseYearCheckBox.Visibility = Visibility.Visible;
                ApplyReleaseYearCheckBox.IsChecked = false;
            }
            else
            {
                ApplyReleaseYearCheckBox.Visibility = Visibility.Collapsed;
            }

            ApplyTrackTitlesCheckBox.IsChecked = false;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            UserAction = CandidateAction.Confirm;
            SelectedMbid = _selectedMbid;

            if (_showFieldCheckboxes && !string.IsNullOrEmpty(_selectedMbid))
            {
                ApplyResult = new MbidApplyResult
                {
                    Mbid = _selectedMbid,
                    ApplyAlbumTitle = ApplyAlbumTitleCheckBox.IsChecked == true,
                    ApplyAlbumArtist = ApplyAlbumArtistCheckBox.IsChecked == true,
                    ApplyReleaseYear = ApplyReleaseYearCheckBox.IsChecked == true,
                    ApplyTrackTitles = ApplyTrackTitlesCheckBox.IsChecked == true,
                    SelectedRelease = _selectedCandidate
                };
            }
            else
            {
                ApplyResult = new MbidApplyResult
                {
                    Mbid = _selectedMbid ?? "",
                    SelectedRelease = _selectedCandidate
                };
            }

            DialogResult = true;
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            UserAction = CandidateAction.Skip;
            DialogResult = true;
            Close();
        }
    }
}
