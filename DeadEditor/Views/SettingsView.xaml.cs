using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            // Show concert database path (read-only info)
            ConcertDbPathTextBox.Text = ConcertLookupService.Instance.ConcertsPath;
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

        // ===== LIBRARY MAINTENANCE =====

        private async void ReenrichButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "This will update FLAC/MP3 venue, city, and state tags in your library using shows.json as the authoritative source.\n\nProceed?",
                "Re-enrich Library?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                return;

            ReenrichButton.IsEnabled = false;
            EnrichProgressText.Text = "Scanning library...";

            try
            {
                // Collect all album folders from both library paths
                var folders = new List<string>();

                if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) &&
                    Directory.Exists(_librarySettings.LibraryRootPath))
                {
                    foreach (var yearFolder in Directory.GetDirectories(_librarySettings.LibraryRootPath))
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(yearFolder), @"^\d{4}$"))
                            continue;
                        folders.AddRange(Directory.GetDirectories(yearFolder));
                    }
                }

                if (!string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath) &&
                    Directory.Exists(_librarySettings.OfficialReleasesPath))
                {
                    // Studio Albums
                    var studioPath = Path.Combine(_librarySettings.OfficialReleasesPath, "Studio Albums");
                    if (Directory.Exists(studioPath))
                        folders.AddRange(Directory.GetDirectories(studioPath));

                    // Series folders (Dave's Picks, etc.)
                    foreach (var seriesFolder in Directory.GetDirectories(_librarySettings.OfficialReleasesPath))
                    {
                        var name = Path.GetFileName(seriesFolder);
                        if (name.Equals("Studio Albums", StringComparison.OrdinalIgnoreCase) ||
                            System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{4}$"))
                            continue;
                        folders.AddRange(Directory.GetDirectories(seriesFolder));
                    }
                }

                // Deduplicate (when both paths point to same directory)
                folders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                int totalFolders = folders.Count;
                int updatedTracks = 0;
                int updatedAlbums = 0;

                // Run the heavy I/O on a background thread
                var enrichResult = await Task.Run(() =>
                {
                    int tracks = 0;
                    int albums = 0;

                    for (int i = 0; i < folders.Count; i++)
                    {
                        var folder = folders[i];
                        int folderIndex = i;

                        // Update progress on UI thread
                        Dispatcher.Invoke(() =>
                        {
                            EnrichProgressText.Text = $"Enriching... {folderIndex + 1} of {totalFolders} albums";
                        });

                        bool albumUpdated = false;

                        // Get all audio files in this folder
                        var audioFiles = new List<string>();
                        try
                        {
                            audioFiles.AddRange(Directory.GetFiles(folder, "*.flac"));
                            audioFiles.AddRange(Directory.GetFiles(folder, "*.mp3"));
                        }
                        catch { continue; }

                        if (audioFiles.Count == 0) continue;

                        // Read the date from the first file's ALBUMDATE tag to look up the show
                        string? albumDate = null;
                        try
                        {
                            // PictureLazy: skip loading embedded artwork (only need ALBUMDATE)
                            using var probe = TagLib.File.Create(audioFiles[0], TagLib.ReadStyle.PictureLazy);
                            albumDate = ReadCustomField(probe, "ALBUMDATE");
                        }
                        catch { }

                        // Fall back to parsing date from folder name
                        if (string.IsNullOrEmpty(albumDate))
                        {
                            var folderName = Path.GetFileName(folder);
                            var dateMatch = System.Text.RegularExpressions.Regex.Match(folderName, @"^(\d{4}-\d{2}-\d{2})");
                            if (dateMatch.Success)
                                albumDate = dateMatch.Groups[1].Value;
                        }

                        if (string.IsNullOrEmpty(albumDate)) continue;

                        var showInfo = ShowLookupService.Instance.GetShowByDate(albumDate);
                        if (showInfo == null) continue;

                        // Determine authoritative values
                        string authVenue = showInfo.Venue;
                        string authCityState = showInfo.FormattedLocation;

                        if (string.IsNullOrEmpty(authVenue) && string.IsNullOrEmpty(authCityState))
                            continue;

                        // Update each audio file
                        foreach (var audioPath in audioFiles)
                        {
                            try
                            {
                                using var file = TagLib.File.Create(audioPath);

                                string? currentVenue = ReadCustomField(file, "VENUE");
                                string? currentCityState = ReadCustomField(file, "CITYSTATE");

                                bool venueChanged = !string.IsNullOrEmpty(authVenue) &&
                                    !string.Equals(currentVenue, authVenue, StringComparison.Ordinal);
                                bool cityStateChanged = !string.IsNullOrEmpty(authCityState) &&
                                    !string.Equals(currentCityState, authCityState, StringComparison.Ordinal);

                                if (!venueChanged && !cityStateChanged) continue;

                                // Write updated tags
                                if (file is TagLib.Flac.File flacFile)
                                {
                                    var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                                    if (xiph != null)
                                    {
                                        if (venueChanged)
                                            xiph.SetField("VENUE", authVenue);
                                        if (cityStateChanged)
                                            xiph.SetField("CITYSTATE", authCityState);
                                    }
                                }
                                else
                                {
                                    var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2, true);
                                    if (id3v2 != null)
                                    {
                                        if (venueChanged)
                                            SetId3v2TextField(id3v2, "VENUE", authVenue);
                                        if (cityStateChanged)
                                            SetId3v2TextField(id3v2, "CITYSTATE", authCityState);
                                    }
                                }

                                file.Save();
                                tracks++;
                                albumUpdated = true;

                                if (venueChanged)
                                    Debug.WriteLine($"[ENRICH] {albumDate}: Venue '{currentVenue}' -> '{authVenue}' in {Path.GetFileName(audioPath)}");
                                if (cityStateChanged)
                                    Debug.WriteLine($"[ENRICH] {albumDate}: CityState '{currentCityState}' -> '{authCityState}' in {Path.GetFileName(audioPath)}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ENRICH] Error updating {audioPath}: {ex.Message}");
                            }
                        }

                        if (albumUpdated)
                            albums++;
                    }

                    return (tracks, albums);
                });

                updatedTracks = enrichResult.tracks;
                updatedAlbums = enrichResult.albums;

                EnrichProgressText.Text = $"Updated {updatedTracks} tracks across {updatedAlbums} albums.";
                StatusText.Text = "Re-enrichment complete. See Debug output for details.";

                // Reload library to reflect corrected venue data
                var shell = Window.GetWindow(this) as ShellWindow;
                var libraryView = shell?.CurrentView is LibraryGridView lgv ? lgv : null;
                libraryView?.ReloadLibrary();
            }
            catch (Exception ex)
            {
                EnrichProgressText.Text = "Error during enrichment.";
                StatusText.Text = $"Error: {ex.Message}";
                Debug.WriteLine($"[ENRICH] Fatal error: {ex}");
            }
            finally
            {
                ReenrichButton.IsEnabled = true;
            }
        }

        private static string? ReadCustomField(TagLib.File file, string fieldName)
        {
            if (file is TagLib.Flac.File flacFile)
            {
                var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                return xiph?.GetFirstField(fieldName);
            }
            else
            {
                var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2);
                if (id3v2 != null)
                {
                    var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, fieldName, false);
                    if (frame?.Text.Length > 0) return frame.Text[0];
                }
                return null;
            }
        }

        private static void SetId3v2TextField(TagLib.Id3v2.Tag tag, string description, string value)
        {
            var existing = TagLib.Id3v2.UserTextInformationFrame.Get(tag, description, false);
            if (existing != null)
                tag.RemoveFrame(existing);

            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(tag, description, true);
            frame.Text = new[] { value ?? "" };
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
