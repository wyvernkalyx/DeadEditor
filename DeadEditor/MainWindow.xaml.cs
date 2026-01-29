using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace DeadEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MetadataService _metadataService;
    private readonly NormalizationService _normalizationService;
    private readonly LibraryImportService _libraryImportService;
    private readonly MusicBrainzService _musicBrainzService;
    private LibrarySettings _librarySettings;
    private List<TrackInfo> _tracks = new();
    private AlbumInfo? _albumInfo;
    private bool _isUpdating = false;
    private TaskCompletionSource<bool>? _notificationResult;

    public MainWindow()
    {
        InitializeComponent();
        _metadataService = new MetadataService();
        _normalizationService = new NormalizationService();
        _libraryImportService = new LibraryImportService(_metadataService);
        _musicBrainzService = new MusicBrainzService("asa4wLQhwJ");

        // Load library settings
        _librarySettings = LibrarySettings.Load();

        // Restore window position
        RestoreWindowPosition();

        // Save window position when closing
        Closing += MainWindow_Closing;
    }

    private void RestoreWindowPosition()
    {
        if (_librarySettings.MainWindowLeft.HasValue && _librarySettings.MainWindowTop.HasValue)
        {
            Left = _librarySettings.MainWindowLeft.Value;
            Top = _librarySettings.MainWindowTop.Value;
        }

        if (_librarySettings.MainWindowWidth.HasValue && _librarySettings.MainWindowHeight.HasValue)
        {
            Width = _librarySettings.MainWindowWidth.Value;
            Height = _librarySettings.MainWindowHeight.Value;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save window position
        _librarySettings.MainWindowLeft = Left;
        _librarySettings.MainWindowTop = Top;
        _librarySettings.MainWindowWidth = Width;
        _librarySettings.MainWindowHeight = Height;
        _librarySettings.Save();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a folder containing audio files (FLAC/MP3)",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Folder Selection"
        };

        // Use a workaround to select folders on Windows
        var folderDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder containing audio files (FLAC/MP3)"
        };

        if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathTextBox.Text = folderDialog.SelectedPath;
            LoadFolder(folderDialog.SelectedPath);
        }
    }

    public async void LoadFolder(string folderPath)
    {
        try
        {
            // Update the text box with the folder path
            FolderPathTextBox.Text = folderPath;

            StatusTextBlock.Text = "Reading files...";

            // Read tracks and sort by disc number, then track number
            _tracks = _metadataService.ReadFolder(folderPath)
                .OrderBy(t => t.DiscNumber)
                .ThenBy(t => t.TrackNumber)
                .ToList();

            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Files", "No audio files (FLAC/MP3) found in the selected folder.");
                StatusTextBlock.Text = "No audio files found";
                return;
            }

            // Read album info
            _albumInfo = _metadataService.ReadAlbumInfo(folderPath, _tracks);

            // Update UI
            RefreshUI();

            StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";
        }
        catch (System.Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error loading folder: {ex.Message}");
            StatusTextBlock.Text = "Error loading folder";
        }
    }

    private void RefreshUI()
    {
        _isUpdating = true;

        // Update album info fields
        if (_albumInfo != null)
        {
            // Set radio buttons based on album type
            LiveRecordingRadio.IsChecked = _albumInfo.Type == AlbumType.Live;
            OfficialReleaseRadio.IsChecked = _albumInfo.Type == AlbumType.OfficialRelease;
            StudioAlbumRadio.IsChecked = _albumInfo.Type == AlbumType.Studio;

            ArtistTextBox.Text = _albumInfo.Artist ?? "Grateful Dead";

            if (_albumInfo.Type == AlbumType.Studio)
            {
                AlbumNameTextBox.Text = _albumInfo.AlbumName ?? "";
                ReleaseYearTextBox.Text = _albumInfo.ReleaseYear?.ToString() ?? "";
                EditionTextBox.Text = _albumInfo.Edition ?? "";
            }
            else
            {
                DateTextBox.Text = _albumInfo.Date ?? "";
                VenueTextBox.Text = _albumInfo.Venue ?? "";
                CityTextBox.Text = _albumInfo.City ?? "";
                StateTextBox.Text = _albumInfo.State ?? "";
                OfficialReleaseTextBox.Text = _albumInfo.OfficialRelease ?? "";
            }

            UpdateFieldVisibility();
            UpdateAlbumPreview();
            UpdateArtworkDisplay();

            // Enable View Info button if info file content exists
            ViewInfoButton.IsEnabled = !string.IsNullOrEmpty(_albumInfo.InfoFileContent);
        }

        // Update track list and previews
        UpdateTrackPreviews();
        TracksDataGrid.ItemsSource = null;
        TracksDataGrid.ItemsSource = _tracks;

        _isUpdating = false;
    }

    private void UpdateTrackPreviews()
    {
        if (_albumInfo == null) return;

        foreach (var track in _tracks)
        {
            // Studio albums don't append dates to track titles
            if (_albumInfo.Type == AlbumType.Studio)
            {
                track.PreviewMetadata = track.GetFinalMetadataTitle(null);
            }
            else
            {
                track.PreviewMetadata = track.GetFinalMetadataTitle(_albumInfo.Date ?? "");
            }
        }
    }

    private void UpdateFieldVisibility()
    {
        if (_albumInfo == null) return;

        bool isStudio = _albumInfo.Type == AlbumType.Studio;
        bool isOfficialRelease = _albumInfo.Type == AlbumType.OfficialRelease;
        bool isLive = _albumInfo.Type == AlbumType.Live;
        bool isBoxSet = _albumInfo.Type == AlbumType.BoxSet;

        // Studio album fields
        AlbumNameLabel.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        AlbumNameTextBox.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        ReleaseYearLabel.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        ReleaseYearTextBox.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        EditionLabel.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        EditionTextBox.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        LookupAlbumButton.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;
        ManualSearchButton.Visibility = isStudio ? Visibility.Visible : Visibility.Collapsed;

        // Live recording fields (show for Live, OfficialRelease, and BoxSet)
        DateLabel.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        DateTextBox.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        VenueLabel.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        VenueTextBox.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        CityLabel.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        CityTextBox.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        StateLabel.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;
        StateTextBox.Visibility = !isStudio ? Visibility.Visible : Visibility.Collapsed;

        // Official Release field (only for OfficialRelease type)
        OfficialReleaseLabel.Visibility = isOfficialRelease ? Visibility.Visible : Visibility.Collapsed;
        OfficialReleaseTextBox.Visibility = isOfficialRelease ? Visibility.Visible : Visibility.Collapsed;

        // Box Set field (only for BoxSet type)
        BoxSetNameLabel.Visibility = isBoxSet ? Visibility.Visible : Visibility.Collapsed;
        BoxSetNameTextBox.Visibility = isBoxSet ? Visibility.Visible : Visibility.Collapsed;

        // Pre-fill box set name with last used value when the field becomes visible
        if (isBoxSet && string.IsNullOrWhiteSpace(BoxSetNameTextBox.Text) && !string.IsNullOrWhiteSpace(_librarySettings.LastBoxSetName))
        {
            _isUpdating = true;
            BoxSetNameTextBox.Text = _librarySettings.LastBoxSetName;
            _isUpdating = false;
        }

        // Auto-select Box Set radio if we pre-filled a box set name for a new import
        // (This handles the case where user is importing a new concert from a remembered box set)
        if (!isBoxSet && !string.IsNullOrEmpty(_librarySettings.LastBoxSetName) &&
            string.IsNullOrWhiteSpace(_albumInfo?.BoxSetName) && _albumInfo?.Type == AlbumType.Live)
        {
            // This appears to be a new import (Live type by default) but we have a remembered box set name
            // Auto-select Box Set type and pre-fill the name
            _isUpdating = true;
            BoxSetRadio.IsChecked = true;
            _albumInfo.Type = AlbumType.BoxSet;
            _isUpdating = false;

            // Refresh visibility to show box set field
            UpdateFieldVisibility();
        }
    }

    private void AlbumType_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _albumInfo == null) return;

        // Determine which album type is selected
        if (LiveRecordingRadio.IsChecked == true)
        {
            _albumInfo.Type = AlbumType.Live;
        }
        else if (OfficialReleaseRadio.IsChecked == true)
        {
            _albumInfo.Type = AlbumType.OfficialRelease;
        }
        else if (StudioAlbumRadio.IsChecked == true)
        {
            _albumInfo.Type = AlbumType.Studio;
        }
        else if (BoxSetRadio.IsChecked == true)
        {
            _albumInfo.Type = AlbumType.BoxSet;
        }

        _albumInfo.IsModified = true;

        UpdateFieldVisibility();
        UpdateAlbumPreview();
        UpdateTrackPreviews();

        // Refresh track list to show updated previews
        TracksDataGrid.ItemsSource = null;
        TracksDataGrid.ItemsSource = _tracks;
    }

    private async void LookupAlbumButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null || _tracks.Count == 0)
        {
            await ShowNotificationAsync("No Tracks", "Please load a folder with audio files (FLAC/MP3) first.");
            return;
        }

        try
        {
            LookupAlbumButton.IsEnabled = false;
            StatusTextBlock.Text = "Looking up album information...";
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;

            // Lookup all releases using audio fingerprinting
            var releases = await _musicBrainzService.LookupAllReleasesAsync(_tracks);

            // Hide progress bar before showing dialogs
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;

            if (releases != null && releases.Count > 0)
            {
                ReleaseOption? selectedRelease = null;

                // ALWAYS show selector dialog for user confirmation (even if only 1 result)
                var message = releases.Count == 1
                    ? "Found 1 release. Please confirm:"
                    : $"Found {releases.Count} releases. Please select:";

                var selectorDialog = new ReleaseSelectorDialog(message, releases)
                {
                    Owner = this
                };

                if (selectorDialog.ShowDialog() == true)
                {
                    selectedRelease = selectorDialog.SelectedRelease;
                }
                else
                {
                    // User cancelled selection
                    StatusTextBlock.Text = "Album lookup cancelled";
                    return;
                }

                if (selectedRelease != null)
                {
                    // Fill in the fields
                    _isUpdating = true;

                    AlbumNameTextBox.Text = selectedRelease.Title;
                    ReleaseYearTextBox.Text = selectedRelease.Year ?? "";
                    ArtistTextBox.Text = selectedRelease.Artist;

                    // Download and set artwork if available
                    if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                    {
                        try
                        {
                            using var httpClient = new System.Net.Http.HttpClient();
                            var imageBytes = await httpClient.GetByteArrayAsync(selectedRelease.ArtworkUrl);

                            _albumInfo.ArtworkData = imageBytes;
                            _albumInfo.ArtworkMimeType = "image/jpeg";

                            UpdateArtworkDisplay();
                        }
                        catch
                        {
                            // Artwork download failed, continue without it
                        }
                    }

                    _albumInfo.AlbumName = selectedRelease.Title;
                    if (int.TryParse(selectedRelease.Year, out var year))
                    {
                        _albumInfo.ReleaseYear = year;
                    }
                    _albumInfo.Artist = selectedRelease.Artist;
                    _albumInfo.IsModified = true;

                    _isUpdating = false;

                    UpdateAlbumPreview();

                    StatusTextBlock.Text = $"Album identified: {selectedRelease.Title} ({selectedRelease.Year})";
                }
            }
            else
            {
                StatusTextBlock.Text = "Album lookup failed";
                await ShowNotificationAsync("Not Found", "Could not identify this album using audio fingerprinting. Please enter the information manually.");
            }
        }
        catch (Exception ex)
        {
            // Hide progress bar before showing dialog
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;
            StatusTextBlock.Text = "Album lookup error";

            await ShowNotificationAsync("Error", $"Error looking up album: {ex.Message}");
        }
        finally
        {
            LookupAlbumButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private async void ManualSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null)
        {
            await ShowNotificationAsync("No Album", "Please load a folder first.");
            return;
        }

        // Get current values from form fields (or empty if not filled)
        var currentAlbum = AlbumNameTextBox.Text;
        var currentArtist = ArtistTextBox.Text;

        // Show search dialog
        var searchDialog = new AlbumSearchDialog(currentAlbum, currentArtist)
        {
            Owner = this
        };

        if (searchDialog.ShowDialog() != true)
        {
            return;
        }

        // Perform search
        try
        {
            LookupAlbumButton.IsEnabled = false;
            ManualSearchButton.IsEnabled = false;
            StatusTextBlock.Text = "Searching MusicBrainz...";
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;

            var releases = await _musicBrainzService.SearchReleasesByNameAsync(
                searchDialog.AlbumName,
                searchDialog.Artist,
                searchDialog.Year);

            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;

            if (releases != null && releases.Count > 0)
            {
                // ALWAYS show selector dialog
                var message = releases.Count == 1
                    ? "Found 1 release. Please confirm:"
                    : $"Found {releases.Count} releases. Please select:";

                var selectorDialog = new ReleaseSelectorDialog(message, releases)
                {
                    Owner = this
                };

                if (selectorDialog.ShowDialog() == true)
                {
                    var selectedRelease = selectorDialog.SelectedRelease;
                    if (selectedRelease != null)
                    {
                        // Fill in the fields (same logic as fingerprint lookup)
                        _isUpdating = true;

                        AlbumNameTextBox.Text = selectedRelease.Title;
                        ReleaseYearTextBox.Text = selectedRelease.Year ?? "";
                        ArtistTextBox.Text = selectedRelease.Artist;

                        // Download artwork
                        if (!string.IsNullOrEmpty(selectedRelease.ArtworkUrl))
                        {
                            try
                            {
                                using var httpClient = new System.Net.Http.HttpClient();
                                var imageBytes = await httpClient.GetByteArrayAsync(selectedRelease.ArtworkUrl);

                                _albumInfo.ArtworkData = imageBytes;
                                _albumInfo.ArtworkMimeType = "image/jpeg";

                                UpdateArtworkDisplay();
                            }
                            catch
                            {
                                // Artwork download failed, continue without it
                            }
                        }

                        _albumInfo.AlbumName = selectedRelease.Title;
                        if (int.TryParse(selectedRelease.Year, out var year))
                        {
                            _albumInfo.ReleaseYear = year;
                        }
                        _albumInfo.Artist = selectedRelease.Artist;
                        _albumInfo.IsModified = true;

                        _isUpdating = false;

                        UpdateAlbumPreview();

                        StatusTextBlock.Text = $"Album selected: {selectedRelease.Title} ({selectedRelease.Year})";
                    }
                }
                else
                {
                    StatusTextBlock.Text = "Search cancelled";
                }
            }
            else
            {
                StatusTextBlock.Text = "No releases found";
                var searchSummary = string.IsNullOrEmpty(searchDialog.Year)
                    ? $"\"{searchDialog.AlbumName}\" by \"{searchDialog.Artist}\""
                    : $"\"{searchDialog.AlbumName}\" by \"{searchDialog.Artist}\" ({searchDialog.Year})";
                await ShowNotificationAsync("Not Found", $"No albums found for {searchSummary}");
            }
        }
        catch (Exception ex)
        {
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;
            StatusTextBlock.Text = "Search error";

            await ShowNotificationAsync("Error", $"Error searching MusicBrainz: {ex.Message}");
        }
        finally
        {
            LookupAlbumButton.IsEnabled = true;
            ManualSearchButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private void UpdateAlbumPreview()
    {
        if (_albumInfo != null)
        {
            AlbumPreviewTextBlock.Text = _albumInfo.AlbumTitle;
        }
    }

    private void AlbumInfo_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating || _albumInfo == null) return;

        _albumInfo.Artist = ArtistTextBox.Text;

        if (_albumInfo.Type == AlbumType.Studio)
        {
            _albumInfo.AlbumName = AlbumNameTextBox.Text;
            if (int.TryParse(ReleaseYearTextBox.Text, out var year))
            {
                _albumInfo.ReleaseYear = year;
            }
            else
            {
                _albumInfo.ReleaseYear = null;
            }
            _albumInfo.Edition = string.IsNullOrWhiteSpace(EditionTextBox.Text) ? null : EditionTextBox.Text;
        }
        else
        {
            _albumInfo.Date = DateTextBox.Text;
            _albumInfo.Venue = VenueTextBox.Text;
            _albumInfo.City = CityTextBox.Text;
            _albumInfo.State = StateTextBox.Text;
            _albumInfo.OfficialRelease = OfficialReleaseTextBox.Text;
            _albumInfo.BoxSetName = string.IsNullOrWhiteSpace(BoxSetNameTextBox.Text) ? null : BoxSetNameTextBox.Text;
        }

        _albumInfo.IsModified = true;

        UpdateAlbumPreview();
        UpdateTrackPreviews();
    }

    private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
        {
            _isUpdating = true;
            SelectedTitleTextBox.Text = selectedTrack.Title;
            SelectedDateTextBox.Text = selectedTrack.PerformanceDate ?? _albumInfo?.Date ?? "";
            SelectedSegueCheckBox.IsChecked = selectedTrack.HasSegue;
            _isUpdating = false;
        }
    }

    private void TracksDataGrid_LoadingRow(object? sender, System.Windows.Controls.DataGridRowEventArgs e)
    {
        UpdateRowBackground(e.Row);
    }

    private void UpdateRowBackground(DataGridRow row)
    {
        if (row.Item is TrackInfo track)
        {
            // Highlight rows with songs not in database (will use original title)
            if (string.IsNullOrEmpty(track.NormalizedTitle) && !string.IsNullOrEmpty(track.Title))
            {
                // Only highlight after normalization has been attempted
                // We can detect this by checking if any track has a normalized title
                bool normalizationAttempted = _tracks.Any(t => !string.IsNullOrEmpty(t.NormalizedTitle));

                if (normalizationAttempted)
                {
                    row.Background = new SolidColorBrush(Color.FromRgb(80, 60, 0)); // Dark yellow/gold for songs not in database
                    row.Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 100)); // Bright yellow text
                }
                else
                {
                    row.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)); // Dark background
                    row.Foreground = Brushes.White;
                }
            }
            else
            {
                row.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)); // Dark background
                row.Foreground = Brushes.White;
            }
        }
    }

    private void UpdateAllRowBackgrounds()
    {
        for (int i = 0; i < TracksDataGrid.Items.Count; i++)
        {
            var row = (DataGridRow?)TracksDataGrid.ItemContainerGenerator.ContainerFromIndex(i);
            if (row != null)
            {
                UpdateRowBackground(row);
            }
        }
    }

    private void SelectedTrack_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
        {
            selectedTrack.Title = SelectedTitleTextBox.Text;
            selectedTrack.PerformanceDate = SelectedDateTextBox.Text;
            selectedTrack.HasSegue = SelectedSegueCheckBox.IsChecked ?? false;
            selectedTrack.IsModified = true;

            // Update the preview metadata to reflect the changes
            UpdateTrackPreviews();

            // Refresh the DataGrid to show changes
            TracksDataGrid.Items.Refresh();
        }
    }

    private async void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FolderPathTextBox.Text))
        {
            await ShowNotificationAsync("No Folder", "Please select a folder first.");
            return;
        }

        LoadFolder(FolderPathTextBox.Text);
    }

    private async void NormalizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        try
        {
            StatusTextBlock.Text = "Normalizing song titles...";
            int matched = _normalizationService.NormalizeAll(_tracks);

            // Update previews with normalized titles
            UpdateTrackPreviews();

            // Refresh DataGrid to show normalized titles
            TracksDataGrid.Items.Refresh();

            // Update row backgrounds to highlight unmatched songs
            UpdateAllRowBackgrounds();

            // Update selected track if there is one
            if (TracksDataGrid.SelectedItem is TrackInfo selectedTrack)
            {
                _isUpdating = true;
                SelectedTitleTextBox.Text = selectedTrack.NormalizedTitle ?? selectedTrack.Title;
                _isUpdating = false;
            }

            StatusTextBlock.Text = $"Normalized {matched} of {_tracks.Count} songs";

            if (matched < _tracks.Count)
            {
                await ShowNotificationAsync(
                    "Normalization Complete",
                    $"Matched {matched} of {_tracks.Count} songs.\n\n" +
                    $"{_tracks.Count - matched} songs not in database (will use original titles).");
            }
        }
        catch (System.Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error normalizing titles: {ex.Message}");
            StatusTextBlock.Text = "Error during normalization";
        }
    }

    private async void RenumberButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        try
        {
            StatusTextBlock.Text = "Renumbering tracks...";

            // Renumber tracks sequentially from 1 to N
            for (int i = 0; i < _tracks.Count; i++)
            {
                _tracks[i].TrackNumber = i + 1;
                _tracks[i].IsModified = true;
            }

            // Update previews to reflect new track numbers
            UpdateTrackPreviews();

            // Refresh DataGrid
            TracksDataGrid.Items.Refresh();

            StatusTextBlock.Text = $"Renumbered {_tracks.Count} tracks (1-{_tracks.Count})";

            await ShowNotificationAsync(
                "Renumber Complete",
                $"Tracks have been renumbered from 1 to {_tracks.Count}.\n\n" +
                "Click 'Write to Files' to save the changes.");
        }
        catch (System.Exception ex)
        {
            await ShowNotificationAsync("Error", $"Error renumbering tracks: {ex.Message}");
            StatusTextBlock.Text = "Error during renumbering";
        }
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0 || _albumInfo == null)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        var confirmed = await ShowNotificationAsync(
            "Confirm Write",
            $"This will write metadata to {_tracks.Count} audio files.\n\n" +
            "This operation cannot be undone. Continue?",
            showYesNo: true);

        if (confirmed)
        {
            try
            {
                StatusTextBlock.Text = "Writing metadata to files...";
                _metadataService.WriteMetadata(_albumInfo, _tracks);
                StatusTextBlock.Text = $"Successfully wrote metadata to {_tracks.Count} files";

                await ShowNotificationAsync(
                    "Success",
                    $"Successfully wrote metadata to {_tracks.Count} files.");

                // Mark as unmodified
                _albumInfo.IsModified = false;
                foreach (var track in _tracks)
                {
                    track.IsModified = false;
                }
            }
            catch (System.Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error writing metadata: {ex.Message}");
                StatusTextBlock.Text = "Error writing metadata";
            }
        }
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Settings is now accessible from LibraryBrowserWindow
        // This menu item should be removed from MainWindow
        MessageBox.Show("Settings can be accessed from the main Library window.", "Information",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Just close this window, don't shut down the whole application
        this.Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Close the import window without importing
        this.Close();
    }

    private void ViewInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null || string.IsNullOrEmpty(_albumInfo.InfoFileContent))
            return;

        // Create and show a non-modal window to display the info file
        var infoWindow = new Window
        {
            Title = $"Info File: {_albumInfo.InfoFileName ?? "Unknown"}",
            Width = 800,
            Height = 600,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Topmost = true  // Keep on top so you can see it while editing
        };

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = _albumInfo.InfoFileContent,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 14,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10),
            TextWrapping = TextWrapping.NoWrap
        };

        infoWindow.Content = textBox;
        infoWindow.Show();  // Non-modal - you can interact with main window while it's open
    }

    // Helper methods for SettingsWindow
    public void UpdateLibraryRootDisplay(string path)
    {
        StatusTextBlock.Text = string.IsNullOrEmpty(path)
            ? "Library root cleared"
            : $"Library root set to: {path}";
    }

    public void ClearCurrentView()
    {
        ClearView();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0 || _albumInfo == null)
        {
            await ShowNotificationAsync("No Files", "Please load files first.");
            return;
        }

        if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
        {
            await ShowNotificationAsync("Library Not Set", "Please set the Library Root folder first.");
            return;
        }

        // Check if Official Releases path is set for official releases
        if (_albumInfo.Type == AlbumType.OfficialRelease && string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath))
        {
            await ShowNotificationAsync("Official Releases Path Not Set",
                "Please set the Official Releases Path in Settings before importing official releases.\n\n" +
                "You can access Settings from the Library Browser window.");
            return;
        }

        // Check if show already exists
        if (_libraryImportService.ShowExistsInLibrary(_librarySettings.LibraryRootPath, _albumInfo, _librarySettings.OfficialReleasesPath))
        {
            var albumDescription = _albumInfo.Type == AlbumType.OfficialRelease
                ? _albumInfo.OfficialRelease
                : $"{_albumInfo.Date} - {_albumInfo.Venue}";

            var overwrite = await ShowNotificationAsync(
                "Show Already Exists",
                $"This album ({albumDescription}) already exists in your library.\n\nOverwrite?",
                showYesNo: true);

            if (!overwrite)
            {
                return;
            }
        }

        // Show confirmation dialog with appropriate destination based on album type
        string destination;
        string structure;

        if (_albumInfo.Type == AlbumType.OfficialRelease)
        {
            destination = _librarySettings.OfficialReleasesPath ?? "";
            structure = $"Series\\{_albumInfo.OfficialRelease}\\";
        }
        else if (_albumInfo.Type == AlbumType.Studio)
        {
            destination = _librarySettings.LibraryRootPath;
            structure = $"Studio Albums\\{_albumInfo.AlbumName} ({_albumInfo.ReleaseYear})\\";
        }
        else
        {
            destination = _librarySettings.LibraryRootPath;
            structure = "Year\\Date - Venue, City, State\\";
        }

        var confirmed = await ShowNotificationAsync(
            "Confirm Import",
            $"Import {_tracks.Count} tracks to library?\n\n" +
            $"Destination: {destination}\n" +
            $"Structure: {structure}\n\n" +
            "Original files will NOT be modified.",
            showYesNo: true);

        if (confirmed)
        {
            try
            {
                // Show progress bar
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.Value = 0;
                StatusTextBlock.Text = "Importing to library...";

                // Create progress reporter
                var progress = new System.Progress<(int current, int total, string message)>(report =>
                {
                    ProgressBar.Value = (report.current * 100.0) / report.total;
                    StatusTextBlock.Text = report.message;
                });

                // Import to library with progress reporting (run in background)
                await Task.Run(() =>
                {
                    _libraryImportService.ImportToLibrary(_librarySettings.LibraryRootPath, _albumInfo, _tracks, progress, _librarySettings.OfficialReleasesPath);
                });

                // Hide progress bar
                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Successfully imported {_tracks.Count} tracks to library";

                // Save box set name to settings for next time (box sets only)
                if (_albumInfo.Type == AlbumType.BoxSet && !string.IsNullOrWhiteSpace(_albumInfo.BoxSetName))
                {
                    _librarySettings.LastBoxSetName = _albumInfo.BoxSetName;
                    _librarySettings.Save();
                }

                // Build success message based on album type
                string successLocation;
                if (_albumInfo.Type == AlbumType.OfficialRelease)
                {
                    successLocation = $"{destination}\\[Series]\\{_albumInfo.OfficialRelease}";
                }
                else if (_albumInfo.Type == AlbumType.Studio)
                {
                    successLocation = $"{destination}\\Studio Albums\\{_albumInfo.AlbumName} ({_albumInfo.ReleaseYear})";
                }
                else
                {
                    var year = System.DateTime.Parse(_albumInfo.Date ?? "2000").Year;
                    successLocation = $"{destination}\\{year}\\{_albumInfo.Date} - {_albumInfo.Venue}, {_albumInfo.City}, {_albumInfo.State}";
                }

                await ShowNotificationAsync(
                    "Import Complete",
                    $"Successfully imported {_tracks.Count} tracks to library!\n\n" +
                    $"Location: {successLocation}");

                // Clear the view after successful import
                ClearView();
            }
            catch (System.Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                await ShowNotificationAsync("Error", $"Error importing to library: {ex.Message}");
                StatusTextBlock.Text = "Error importing to library";
            }
        }
    }

    private void ClearView()
    {
        _tracks.Clear();
        _albumInfo = null;

        // Clear UI fields
        FolderPathTextBox.Text = string.Empty;
        ArtistTextBox.Text = string.Empty;
        DateTextBox.Text = string.Empty;
        VenueTextBox.Text = string.Empty;
        CityTextBox.Text = string.Empty;
        StateTextBox.Text = string.Empty;
        OfficialReleaseTextBox.Text = string.Empty;
        AlbumPreviewTextBlock.Text = string.Empty;
        SelectedTitleTextBox.Text = string.Empty;
        SelectedDateTextBox.Text = string.Empty;
        SelectedSegueCheckBox.IsChecked = false;

        // Clear artwork
        ArtworkImage.Source = null;
        ArtworkImage.Visibility = Visibility.Collapsed;

        // Clear track list
        TracksDataGrid.ItemsSource = null;

        StatusTextBlock.Text = "Ready";
    }


    // Artwork management
    private void UpdateArtworkDisplay()
    {
        if (_albumInfo?.ArtworkData != null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(_albumInfo.ArtworkData);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                ArtworkImage.Source = bitmap;
                ArtworkImage.Visibility = Visibility.Visible;
                NoArtworkText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // If artwork fails to load, show no artwork
                ArtworkImage.Source = null;
                ArtworkImage.Visibility = Visibility.Collapsed;
                NoArtworkText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ArtworkImage.Source = null;
            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
        }
    }

    private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null) return;

        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Album Artwork",
            Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                // Read image file
                var imageData = File.ReadAllBytes(openFileDialog.FileName);

                // Determine MIME type
                var extension = Path.GetExtension(openFileDialog.FileName).ToLower();
                var mimeType = extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "image/jpeg"
                };

                // Store artwork in album info
                _albumInfo.ArtworkData = imageData;
                _albumInfo.ArtworkMimeType = mimeType;
                _albumInfo.IsModified = true;

                // Update display
                UpdateArtworkDisplay();

                StatusTextBlock.Text = "Artwork loaded";
            }
            catch (System.Exception ex)
            {
                StatusTextBlock.Text = $"Error loading artwork: {ex.Message}";
            }
        }
    }

    private void RemoveArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_albumInfo == null) return;

        _albumInfo.ArtworkData = null;
        _albumInfo.ArtworkMimeType = null;
        _albumInfo.IsModified = true;

        UpdateArtworkDisplay();
        StatusTextBlock.Text = "Artwork removed";
    }


    // Notification system to replace MessageBox
    private Task<bool> ShowNotificationAsync(string title, string message, bool showYesNo = false)
    {
        _notificationResult = new TaskCompletionSource<bool>();

        NotificationTitle.Text = title;
        NotificationMessage.Text = message;

        if (showYesNo)
        {
            NotificationYesButton.Visibility = Visibility.Visible;
            NotificationNoButton.Visibility = Visibility.Visible;
            NotificationOkButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotificationYesButton.Visibility = Visibility.Collapsed;
            NotificationNoButton.Visibility = Visibility.Collapsed;
            NotificationOkButton.Visibility = Visibility.Visible;
        }

        NotificationPanel.Visibility = Visibility.Visible;

        return _notificationResult.Task;
    }

    private void HideNotification()
    {
        NotificationPanel.Visibility = Visibility.Collapsed;
    }

    private void NotificationYes_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(true);
    }

    private void NotificationNo_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(false);
    }

    private void NotificationOk_Click(object sender, RoutedEventArgs e)
    {
        HideNotification();
        _notificationResult?.SetResult(true);
    }

    private void NotificationPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close notification if clicking outside the dialog
        if (e.OriginalSource == NotificationPanel)
        {
            HideNotification();
            _notificationResult?.SetResult(false);
        }
    }
}
