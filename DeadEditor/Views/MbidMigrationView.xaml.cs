using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace DeadEditor.Views
{
    public partial class MbidMigrationView : UserControl
    {
        private MbidMigrationService? _migrationService;
        private MusicBrainzService? _musicBrainzService;
        private LibrarySettings _librarySettings;
        private List<LibraryShow> _libraryShows = new();
        private MigrationState? _existingState;

        public MbidMigrationView(List<LibraryShow> libraryShows)
        {
            InitializeComponent();
            _librarySettings = LibrarySettings.Load();
            _libraryShows = libraryShows;

            RefreshStartScreen();
        }

        private void RefreshStartScreen()
        {
            var (total, alreadyTagged, needsMigration) = MbidMigrationService.GetAlbumCounts(_libraryShows);
            TotalCountText.Text = total.ToString();
            TaggedCountText.Text = alreadyTagged.ToString();
            NeedsMigrationText.Text = needsMigration.ToString();

            StartButton.IsEnabled = needsMigration > 0;

            // Check for existing state
            _existingState = MbidMigrationService.LoadState();
            if (_existingState != null && _existingState.Albums.Count > 0)
            {
                int completedCount = _existingState.Albums.Count;
                string modeLabel = _existingState.DryRun ? " (dry-run)" : "";
                ResumeText.Text = $"Previous migration{modeLabel} in progress — {completedCount} album(s) processed.";
                ResumeBanner.Visibility = Visibility.Visible;
            }
            else
            {
                ResumeBanner.Visibility = Visibility.Collapsed;
            }

            ShowPanel(StartPanel);
        }

        private void ShowPanel(StackPanel panel)
        {
            StartPanel.Visibility = panel == StartPanel ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanel.Visibility = panel == ProgressPanel ? Visibility.Visible : Visibility.Collapsed;
            CompletionPanel.Visibility = panel == CompletionPanel ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== START SCREEN =====

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMigration(resume: false);
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            StartMigration(resume: true);
        }

        private void StartFreshButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will discard the previous migration state and start over.\n\nContinue?",
                "Start Fresh?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            MbidMigrationService.DeleteState();
            _existingState = null;
            ResumeBanner.Visibility = Visibility.Collapsed;
        }

        private async void StartMigration(bool resume)
        {
            bool dryRun = DryRunCheckBox.IsChecked == true;

            // Set up services
            _musicBrainzService = new MusicBrainzService("asa4wLQhwJ", _librarySettings);
            _migrationService = new MbidMigrationService(_musicBrainzService, _librarySettings);

            // Wire events
            _migrationService.ProgressChanged += OnProgressChanged;
            _migrationService.CandidateReviewRequested += OnCandidateReviewRequested;
            _migrationService.MigrationCompleted += OnMigrationCompleted;

            // Switch to progress screen
            ShowPanel(ProgressPanel);

            if (dryRun)
                DryRunBanner.Visibility = Visibility.Visible;
            else
                DryRunBanner.Visibility = Visibility.Collapsed;

            // Check fpcalc
            if (string.IsNullOrEmpty(_librarySettings.FpcalcPath) || !File.Exists(_librarySettings.FpcalcPath))
            {
                FpcalcWarningText.Text = "fpcalc not configured — using name search only. Configure fpcalc.exe path in Settings for fingerprint matching.";
                FpcalcWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                FpcalcWarningText.Visibility = Visibility.Collapsed;
            }

            try
            {
                await _migrationService.RunAsync(_libraryShows, dryRun, resume);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Migration error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowPanel(StartPanel);
            }
        }

        // ===== PROGRESS EVENTS =====

        private void OnProgressChanged(object? sender, MigrationProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var folderName = Path.GetFileName(e.CurrentAlbum.FolderPaths.FirstOrDefault() ?? "");
                var albumName = !string.IsNullOrEmpty(e.CurrentAlbum.AlbumName) ? e.CurrentAlbum.AlbumName
                    : !string.IsNullOrEmpty(e.CurrentAlbum.OfficialRelease) ? e.CurrentAlbum.OfficialRelease
                    : folderName;
                CurrentAlbumText.Text = $"{albumName}\n{folderName}";

                if (e.Total > 0)
                {
                    MigrationProgressBar.Maximum = e.Total;
                    MigrationProgressBar.Value = e.Completed;
                    int pct = (int)(100.0 * e.Completed / e.Total);
                    ProgressText.Text = $"{e.Completed} of {e.Total} ({pct}%)";
                }

                MatchedText.Text = $"Matched: {e.Matched}";
                SkippedText.Text = $"Skipped: {e.Skipped}";
                FailedText.Text = $"Failed: {e.Failed}";
            });
        }

        private void OnCandidateReviewRequested(object? sender, CandidateReviewEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var dialog = new MbidCandidateDialog(
                    e.Album, e.Candidates, e.DryRunPreSelectedMbid, e.Warning);
                dialog.Owner = Window.GetWindow(this);

                var result = dialog.ShowDialog();

                if (result == true)
                {
                    e.UserAction = dialog.UserAction;
                    e.SelectedMbid = dialog.SelectedMbid;
                }
                else
                {
                    e.UserAction = CandidateAction.Skip;
                }
                e.IsResolved = true;
            });
        }

        private void OnMigrationCompleted(object? sender, MigrationCompleteEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.WasPaused)
                {
                    // Migration paused — go back to start screen with updated counts
                    RefreshStartScreen();
                    return;
                }

                // Show completion screen
                ShowPanel(CompletionPanel);

                if (e.WasDryRun)
                {
                    CompletionTitle.Text = "Dry Run Complete";
                    CompletionDryRunBanner.Visibility = Visibility.Visible;
                    DryRunMessage.Visibility = Visibility.Visible;
                }
                else
                {
                    CompletionTitle.Text = "Migration Complete";
                    CompletionDryRunBanner.Visibility = Visibility.Collapsed;
                    DryRunMessage.Visibility = Visibility.Collapsed;
                }

                SummaryMatchedText.Text = $"Matched and tagged: {e.Matched}";
                SummarySkippedText.Text = $"Skipped by user: {e.Skipped}";
                SummaryAlreadyTaggedText.Text = $"Already tagged (pre-existing): {e.AlreadyTagged}";
                SummaryFailedText.Text = $"Failed (no candidates): {e.Failed}";

                if (e.Failures.Count > 0)
                {
                    FailuresExpander.Visibility = Visibility.Visible;
                    FailuresPanel.Children.Clear();
                    foreach (var (folderPath, reason) in e.Failures)
                    {
                        var text = new TextBlock
                        {
                            Text = $"{Path.GetFileName(folderPath)} — {reason}",
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x99, 0x99)),
                            Margin = new Thickness(0, 2, 0, 2),
                            TextWrapping = TextWrapping.Wrap
                        };
                        FailuresPanel.Children.Add(text);
                    }
                }
                else
                {
                    FailuresExpander.Visibility = Visibility.Collapsed;
                }
            });
        }

        // ===== CONTROL BUTTONS =====

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _migrationService?.Pause();
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pausing...";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _migrationService?.Cancel();
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Cancelling...";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back to Settings
            var shell = Window.GetWindow(this) as ShellWindow;
            shell?.NavigateToSettings();
        }
    }
}
