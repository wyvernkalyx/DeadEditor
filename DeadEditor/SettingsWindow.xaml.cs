using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.IO;
using System.Windows;

namespace DeadEditor
{
    public partial class SettingsWindow : Window
    {
        private LibrarySettings _librarySettings;
        private LibraryBrowserWindow _libraryWindow;
        private NormalizationService _normalizationService;

        public SettingsWindow(LibraryBrowserWindow libraryWindow, LibrarySettings librarySettings, NormalizationService normalizationService)
        {
            InitializeComponent();
            _libraryWindow = libraryWindow;
            _librarySettings = librarySettings;
            _normalizationService = normalizationService;

            // Load current settings
            LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;
            OfficialReleasesTextBox.Text = _librarySettings.OfficialReleasesPath;
            FpcalcPathTextBox.Text = _librarySettings.FpcalcPath;
            PrimaryArtistTextBox.Text = _librarySettings.PrimaryArtistName;
        }

        private void BrowseLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Library Root Folder (Audience Recordings)",
                SelectedPath = _librarySettings.LibraryRootPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _librarySettings.LibraryRootPath = folderDialog.SelectedPath;
                _librarySettings.Save();
                LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;

                // Update library window
                _libraryWindow.UpdateLibraryRootDisplay(_librarySettings.LibraryRootPath);
            }
        }

        private void BrowseOfficialReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Official Releases Folder (Dave's Picks, Road Trips, etc.)",
                SelectedPath = _librarySettings.OfficialReleasesPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _librarySettings.OfficialReleasesPath = folderDialog.SelectedPath;
                _librarySettings.Save();
                OfficialReleasesTextBox.Text = _librarySettings.OfficialReleasesPath;

                // Update library window to reload with new path
                _libraryWindow.UpdateLibraryRootDisplay(_librarySettings.LibraryRootPath);
            }
        }

        private void BrowseFpcalcButton_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select fpcalc.exe (Chromaprint)",
                Filter = "Executable Files|*.exe",
                FileName = !string.IsNullOrEmpty(_librarySettings.FpcalcPath) ? Path.GetFileName(_librarySettings.FpcalcPath) : "fpcalc.exe",
                InitialDirectory = !string.IsNullOrEmpty(_librarySettings.FpcalcPath) ? Path.GetDirectoryName(_librarySettings.FpcalcPath) : ""
            };

            if (fileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _librarySettings.FpcalcPath = fileDialog.FileName;
                _librarySettings.Save();
                FpcalcPathTextBox.Text = _librarySettings.FpcalcPath;

                // Reset dismissed warning flag so user knows fingerprinting is now available
                _librarySettings.DismissedFpcalcWarning = false;
                _librarySettings.Save();
            }
        }

        private async void ResetDataButton_Click(object sender, RoutedEventArgs e)
        {
            // Show warning confirmation
            var result = System.Windows.MessageBox.Show(
                "This will remove all imported library data:\n\n" +
                "• Move all library files to the Recycle Bin\n\n" +
                "Your settings, paths, and configuration will NOT be affected.\n\n" +
                "Continue?",
                "Reset Library Data?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                int deletedItems = 0;

                // 1. Delete library contents (move to recycle bin)
                if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) &&
                    Directory.Exists(_librarySettings.LibraryRootPath))
                {
                    var directories = Directory.GetDirectories(_librarySettings.LibraryRootPath);
                    var files = Directory.GetFiles(_librarySettings.LibraryRootPath);

                    // Move directories to recycle bin
                    foreach (var dir in directories)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            dir,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }

                    // Move files to recycle bin
                    foreach (var file in files)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            file,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                }

                // 2. Delete official releases contents (move to recycle bin)
                if (!string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath) &&
                    Directory.Exists(_librarySettings.OfficialReleasesPath))
                {
                    var directories = Directory.GetDirectories(_librarySettings.OfficialReleasesPath);
                    var files = Directory.GetFiles(_librarySettings.OfficialReleasesPath);

                    // Move directories to recycle bin
                    foreach (var dir in directories)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            dir,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }

                    // Move files to recycle bin
                    foreach (var file in files)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            file,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                }

                // 3. Clear the current view in library window (but keep paths)
                _libraryWindow.ClearCurrentView();

                // Note: Settings and paths are preserved - only library data is cleared

                System.Windows.MessageBox.Show(
                    $"Successfully reset library data!\n\n" +
                    $"• {deletedItems} items moved to Recycle Bin\n" +
                    $"• Your settings and paths have been preserved\n\n" +
                    $"You can restore files from the Recycle Bin if needed.",
                    "Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Close this settings window and bring focus back to library window
                this.Close();
                _libraryWindow.Activate();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error resetting data: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddSongDialog(_normalizationService);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void ManageSongsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageSongsDialog(_normalizationService);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Save primary artist setting
            _librarySettings.PrimaryArtistName = PrimaryArtistTextBox.Text;
            _librarySettings.Save();

            this.Close();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Suppress alert chime when Enter is pressed on buttons
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;

                // If a button has focus, invoke its click handler manually
                if (e.Source is System.Windows.Controls.Button button)
                {
                    // Trigger the button's Click event using PerformClick equivalent
                    var clickEvent = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
                    button.RaiseEvent(clickEvent);
                }
            }
        }
    }
}
