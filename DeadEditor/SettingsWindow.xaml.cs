using DeadEditor.Models;
using System;
using System.IO;
using System.Windows;

namespace DeadEditor
{
    public partial class SettingsWindow : Window
    {
        private LibrarySettings _librarySettings;
        private MainWindow _mainWindow;

        public SettingsWindow(MainWindow mainWindow, LibrarySettings librarySettings)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _librarySettings = librarySettings;

            // Load current settings
            LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;
        }

        private void BrowseLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Library Root Folder",
                SelectedPath = _librarySettings.LibraryRootPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _librarySettings.LibraryRootPath = folderDialog.SelectedPath;
                _librarySettings.Save();
                LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;

                // Update main window
                _mainWindow.UpdateLibraryRootDisplay(_librarySettings.LibraryRootPath);
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

                // 2. Clear the current view in main window
                _mainWindow.ClearCurrentView();

                // 3. Reset library settings
                _librarySettings.LibraryRootPath = "";
                _librarySettings.Save();
                LibraryRootTextBox.Text = "";

                // Update main window
                _mainWindow.UpdateLibraryRootDisplay("");

                System.Windows.MessageBox.Show(
                    $"Successfully reset all data!\n\n" +
                    $"• {deletedItems} items moved to Recycle Bin\n" +
                    $"• Library settings cleared\n\n" +
                    $"You can restore files from the Recycle Bin if needed.",
                    "Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
