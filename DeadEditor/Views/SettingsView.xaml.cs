using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.IO;
using System.Windows;

namespace DeadEditor
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private LibrarySettings _librarySettings;
        private NormalizationService _normalizationService;

        public SettingsView()
        {
            InitializeComponent();
            _librarySettings = LibrarySettings.Load();
            _normalizationService = new NormalizationService();
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            // Reload settings in case they changed since construction
            _librarySettings = LibrarySettings.Load();

            LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;
            OfficialReleasesTextBox.Text = _librarySettings.OfficialReleasesPath;
            FpcalcPathTextBox.Text = _librarySettings.FpcalcPath;
            PrimaryArtistTextBox.Text = _librarySettings.PrimaryArtistName;
        }

        // ===== PATH SELECTION =====

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
                StatusText.Text = "Library root path updated";
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
                StatusText.Text = "Official releases path updated";
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
                _librarySettings.DismissedFpcalcWarning = false;
                _librarySettings.Save();
                FpcalcPathTextBox.Text = _librarySettings.FpcalcPath;
                StatusText.Text = "fpcalc.exe path updated";
            }
        }

        // ===== SAVE SETTINGS =====

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _librarySettings.PrimaryArtistName = PrimaryArtistTextBox.Text;
            _librarySettings.Save();

            // Refresh library if the shell has a library view
            var shell = Window.GetWindow(this) as ShellWindow;
            if (shell != null)
            {
                // Force library reload to pick up any path changes
                var libraryView = GetLibraryView(shell);
                libraryView?.ReloadLibrary();
            }

            StatusText.Text = "Settings saved";
        }

        private LibraryGridView? GetLibraryView(ShellWindow shell)
        {
            // Access the library view via reflection or navigation if available
            // The shell creates _libraryView on first access
            if (shell.CurrentView is LibraryGridView lgv)
                return lgv;
            return null;
        }

        // ===== SONG DATABASE =====

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddSongDialog(_normalizationService);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        private void ManageSongsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageSongsDialog(_normalizationService);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        // ===== DATA MANAGEMENT =====

        private void ResetDataButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "This will remove all imported library data:\n\n" +
                "\u2022 Move all library files to the Recycle Bin\n\n" +
                "Your settings, paths, and configuration will NOT be affected.\n\n" +
                "Continue?",
                "Reset Library Data?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                int deletedItems = 0;

                // Delete library root contents
                if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) &&
                    Directory.Exists(_librarySettings.LibraryRootPath))
                {
                    foreach (var dir in Directory.GetDirectories(_librarySettings.LibraryRootPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            dir,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                    foreach (var file in Directory.GetFiles(_librarySettings.LibraryRootPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            file,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                }

                // Delete official releases contents
                if (!string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath) &&
                    Directory.Exists(_librarySettings.OfficialReleasesPath))
                {
                    foreach (var dir in Directory.GetDirectories(_librarySettings.OfficialReleasesPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            dir,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                    foreach (var file in Directory.GetFiles(_librarySettings.OfficialReleasesPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            file,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deletedItems++;
                    }
                }

                StatusText.Text = $"Reset complete: {deletedItems} items moved to Recycle Bin";

                // Refresh library view
                var shell = Window.GetWindow(this) as ShellWindow;
                var libraryView = shell?.CurrentView is LibraryGridView lgv ? lgv : null;
                libraryView?.ReloadLibrary();

                System.Windows.MessageBox.Show(
                    $"Successfully reset library data!\n\n" +
                    $"\u2022 {deletedItems} items moved to Recycle Bin\n" +
                    $"\u2022 Your settings and paths have been preserved\n\n" +
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
    }
}
