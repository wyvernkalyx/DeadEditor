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

        private async void ResetDataButton_Click(object sender, RoutedEventArgs e)
        {
            // Show warning confirmation
            var result = System.Windows.MessageBox.Show(
                "This will:\n" +
                "• Move all files in your library to the Recycle Bin\n" +
                "• Reset the songs database\n" +
                "• Clear library settings\n\n" +
                "This cannot be easily undone. Continue?",
                "Reset All Data?",
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

                // 3. Clear the current view in library window
                _libraryWindow.ClearCurrentView();

                // 4. Reset library settings
                _librarySettings.LibraryRootPath = "";
                _librarySettings.OfficialReleasesPath = "";
                _librarySettings.Save();
                LibraryRootTextBox.Text = "";
                OfficialReleasesTextBox.Text = "";

                // Update library window
                _libraryWindow.UpdateLibraryRootDisplay("");

                System.Windows.MessageBox.Show(
                    $"Successfully reset all data!\n\n" +
                    $"• {deletedItems} items moved to Recycle Bin\n" +
                    $"• Library settings cleared\n\n" +
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
    }
}
