using DeadEditor.Services;
using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;

namespace DeadEditor
{
    public partial class AdvancedSearchDialog : Window
    {
        private readonly NormalizationService _normalizationService;
        private readonly MetadataService _metadataService;
        private readonly LibrarySettings _librarySettings;
        private readonly LibraryBrowserWindow? _libraryBrowserWindow;
        private List<SongCheckItem> _songCheckItems = new();
        private List<SequenceItem> _sequenceItems = new();
        private List<CheckBox> _allSongCheckBoxes = new();
        private List<CheckBox> _allExcludeSongCheckBoxes = new();
        private string _songFilter = "";
        private string _excludeSongFilter = "";

        public List<string> SelectedSongs { get; private set; } = new();
        public List<string> ExcludedSongs { get; private set; } = new();
        public List<string> SongSequence { get; private set; } = new();

        public AdvancedSearchDialog(NormalizationService normalizationService,
                                     MetadataService metadataService,
                                     LibrarySettings librarySettings,
                                     LibraryBrowserWindow? libraryBrowserWindow = null)
        {
            InitializeComponent();
            _normalizationService = normalizationService;
            _metadataService = metadataService;
            _librarySettings = librarySettings;
            _libraryBrowserWindow = libraryBrowserWindow;
            LoadSongs();
        }

        private void LoadSongs()
        {
            var allSongs = _normalizationService.GetAllTitles();

            // Populate song checklist (include)
            _songCheckItems = allSongs.Select(song => new SongCheckItem { Song = song, IsChecked = false }).ToList();

            foreach (var item in _songCheckItems)
            {
                var checkBox = new CheckBox
                {
                    Content = item.Song,
                    Margin = new Thickness(5),
                    FontSize = 12
                };
                checkBox.Checked += SongCheckBox_Changed;
                checkBox.Unchecked += SongCheckBox_Changed;
                checkBox.Tag = item;

                _allSongCheckBoxes.Add(checkBox);
                SongCheckListPanel.Children.Add(checkBox);
            }

            // Populate exclude song checklist
            foreach (var song in allSongs)
            {
                var checkBox = new CheckBox
                {
                    Content = song,
                    Margin = new Thickness(5),
                    FontSize = 12
                };
                checkBox.Checked += ExcludeSongCheckBox_Changed;
                checkBox.Unchecked += ExcludeSongCheckBox_Changed;

                _allExcludeSongCheckBoxes.Add(checkBox);
                ExcludeSongCheckListPanel.Children.Add(checkBox);
            }

            // Populate sequence combo box
            SequenceSongComboBox.ItemsSource = allSongs;
        }

        private void SongFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _songFilter = SongFilterTextBox.Text?.ToLowerInvariant() ?? "";
            ClearFilterButton.Visibility = string.IsNullOrEmpty(_songFilter) ? Visibility.Collapsed : Visibility.Visible;
            ApplySongFilter();
        }

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            SongFilterTextBox.Text = "";
            _songFilter = "";
            ClearFilterButton.Visibility = Visibility.Collapsed;
            ApplySongFilter();
        }

        private void ApplySongFilter()
        {
            SongCheckListPanel.Children.Clear();

            foreach (var checkBox in _allSongCheckBoxes)
            {
                var songName = checkBox.Content?.ToString()?.ToLowerInvariant() ?? "";

                if (string.IsNullOrEmpty(_songFilter) || songName.Contains(_songFilter))
                {
                    SongCheckListPanel.Children.Add(checkBox);
                }
            }
        }

        private void SongCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectedSongsCount();
        }

        private void UpdateSelectedSongsCount()
        {
            // Count all checked boxes (including filtered out ones)
            int count = _allSongCheckBoxes.Count(cb => cb.IsChecked == true);
            SelectedSongsCount.Text = count > 0 ? $"{count} song{(count == 1 ? "" : "s")} selected" : "";
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Select all visible (filtered) songs
            foreach (var child in SongCheckListPanel.Children)
            {
                if (child is CheckBox cb)
                {
                    cb.IsChecked = true;
                }
            }
            UpdateSelectedSongsCount();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear all checkboxes (not just visible ones)
            foreach (var cb in _allSongCheckBoxes)
            {
                cb.IsChecked = false;
            }
            UpdateSelectedSongsCount();
        }

        private void AddToSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (SequenceSongComboBox.SelectedItem is string song)
            {
                _sequenceItems.Add(new SequenceItem
                {
                    Index = _sequenceItems.Count + 1,
                    Song = song
                });

                RefreshSequenceList();
            }
        }

        private void RemoveFromSequence_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string song)
            {
                var item = _sequenceItems.FirstOrDefault(i => i.Song == song);
                if (item != null)
                {
                    _sequenceItems.Remove(item);

                    // Re-index
                    for (int i = 0; i < _sequenceItems.Count; i++)
                    {
                        _sequenceItems[i].Index = i + 1;
                    }

                    RefreshSequenceList();
                }
            }
        }

        private void ClearSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            _sequenceItems.Clear();
            RefreshSequenceList();
        }

        private void RefreshSequenceList()
        {
            SequenceListBox.ItemsSource = null;
            SequenceListBox.ItemsSource = _sequenceItems;

            SequenceEmptyText.Visibility = _sequenceItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ExcludeSongFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _excludeSongFilter = ExcludeSongFilterTextBox.Text?.ToLowerInvariant() ?? "";
            ClearExcludeFilterButton.Visibility = string.IsNullOrEmpty(_excludeSongFilter) ? Visibility.Collapsed : Visibility.Visible;
            ApplyExcludeSongFilter();
        }

        private void ClearExcludeFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ExcludeSongFilterTextBox.Text = "";
            _excludeSongFilter = "";
            ClearExcludeFilterButton.Visibility = Visibility.Collapsed;
            ApplyExcludeSongFilter();
        }

        private void ApplyExcludeSongFilter()
        {
            ExcludeSongCheckListPanel.Children.Clear();

            foreach (var checkBox in _allExcludeSongCheckBoxes)
            {
                var songName = checkBox.Content?.ToString()?.ToLowerInvariant() ?? "";

                if (string.IsNullOrEmpty(_excludeSongFilter) || songName.Contains(_excludeSongFilter))
                {
                    ExcludeSongCheckListPanel.Children.Add(checkBox);
                }
            }
        }

        private void ExcludeSongCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateExcludedSongsCount();
        }

        private void UpdateExcludedSongsCount()
        {
            int count = _allExcludeSongCheckBoxes.Count(cb => cb.IsChecked == true);
            ExcludedSongsCount.Text = count > 0 ? $"{count} song{(count == 1 ? "" : "s")} excluded" : "";
        }

        private void ExcludeSelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in ExcludeSongCheckListPanel.Children)
            {
                if (child is CheckBox cb)
                {
                    cb.IsChecked = true;
                }
            }
            UpdateExcludedSongsCount();
        }

        private void ExcludeClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var cb in _allExcludeSongCheckBoxes)
            {
                cb.IsChecked = false;
            }
            UpdateExcludedSongsCount();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Collect selected songs from ALL checkboxes (not just visible/filtered ones)
            SelectedSongs.Clear();
            foreach (var cb in _allSongCheckBoxes)
            {
                if (cb.IsChecked == true)
                {
                    SelectedSongs.Add(cb.Content.ToString() ?? "");
                }
            }

            // Collect excluded songs
            ExcludedSongs.Clear();
            foreach (var cb in _allExcludeSongCheckBoxes)
            {
                if (cb.IsChecked == true)
                {
                    ExcludedSongs.Add(cb.Content.ToString() ?? "");
                }
            }

            // Collect sequence
            SongSequence = _sequenceItems.Select(i => i.Song).ToList();

            // Validate
            if (!SelectedSongs.Any() && !ExcludedSongs.Any() && !SongSequence.Any())
            {
                MessageBox.Show("Please select some songs, excluded songs, or create a sequence to search for.",
                    "No Search Criteria", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Track Search Tab Methods
        private void TrackSearchButton_Click(object sender, RoutedEventArgs e)
        {
            var searchDate = TrackDateTextBox.Text?.Trim();
            var searchVenue = TrackVenueTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(searchDate) && string.IsNullOrEmpty(searchVenue))
            {
                MessageBox.Show("Please enter a date or venue to search.", "No Search Criteria",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var results = SearchTracksInLibrary(searchDate, searchVenue);

            if (results.Count > 0)
            {
                TrackResultsDataGrid.ItemsSource = results;
                TrackResultsDataGrid.Visibility = Visibility.Visible;
                TrackResultsEmptyText.Visibility = Visibility.Collapsed;
            }
            else
            {
                TrackResultsDataGrid.ItemsSource = null;
                TrackResultsDataGrid.Visibility = Visibility.Collapsed;
                TrackResultsEmptyText.Text = "No tracks found matching your search criteria.";
                TrackResultsEmptyText.Visibility = Visibility.Visible;
            }
        }

        private List<TrackSearchResult> SearchTracksInLibrary(string? searchDate, string? searchVenue)
        {
            var results = new List<TrackSearchResult>();

            // Search audience recordings
            if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) && Directory.Exists(_librarySettings.LibraryRootPath))
            {
                SearchInFolder(_librarySettings.LibraryRootPath, AlbumType.Live, searchDate, searchVenue, results);
            }

            // Search official releases
            if (!string.IsNullOrEmpty(_librarySettings.OfficialReleasesPath) && Directory.Exists(_librarySettings.OfficialReleasesPath))
            {
                SearchInFolder(_librarySettings.OfficialReleasesPath, AlbumType.OfficialRelease, searchDate, searchVenue, results);
            }

            // Search studio albums
            var studioAlbumsPath = Path.Combine(_librarySettings.LibraryRootPath, "Studio Albums");
            if (Directory.Exists(studioAlbumsPath))
            {
                SearchInFolder(studioAlbumsPath, AlbumType.Studio, searchDate, searchVenue, results);
            }

            return results;
        }

        private void SearchInFolder(string rootPath, AlbumType albumType, string? searchDate, string? searchVenue, List<TrackSearchResult> results)
        {
            try
            {
                var albumFolders = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);

                foreach (var albumFolder in albumFolders)
                {
                    var audioFiles = Directory.GetFiles(albumFolder, "*.flac")
                        .Concat(Directory.GetFiles(albumFolder, "*.mp3"))
                        .ToList();

                    if (audioFiles.Count == 0) continue;

                    // Read tracks from this album
                    var tracks = _metadataService.ReadFolder(albumFolder);
                    var folderName = Path.GetFileName(albumFolder);

                    foreach (var track in tracks)
                    {
                        bool matches = false;

                        // Check if track matches search criteria
                        if (!string.IsNullOrEmpty(searchDate))
                        {
                            // For live albums, check album date
                            if (albumType == AlbumType.Live && folderName.Contains(searchDate))
                            {
                                matches = true;
                            }
                            // For all albums, check embedded dates in track titles
                            else if (TrackContainsDate(track.Title, searchDate))
                            {
                                matches = true;
                            }
                        }

                        if (!string.IsNullOrEmpty(searchVenue) && !matches)
                        {
                            // Check if venue appears in folder name or track title
                            if (folderName.IndexOf(searchVenue, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                track.Title.IndexOf(searchVenue, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matches = true;
                            }
                        }

                        if (matches)
                        {
                            results.Add(new TrackSearchResult
                            {
                                TrackTitle = track.Title,
                                AlbumTitle = folderName,
                                AlbumType = albumType.ToString(),
                                AlbumPath = albumFolder
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue searching
                System.Diagnostics.Debug.WriteLine($"Error searching folder {rootPath}: {ex.Message}");
            }
        }

        private bool TrackContainsDate(string trackTitle, string searchDate)
        {
            // Look for patterns like (yyyy-MM-dd) or [yyyy-MM-dd] in the title
            var datePattern = @"[\(\[]" + Regex.Escape(searchDate) + @"[\)\]]";
            return Regex.IsMatch(trackTitle, datePattern);
        }

        private void TrackResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TrackResultsDataGrid.SelectedItem is TrackSearchResult result)
            {
                // Close this dialog
                DialogResult = false;
                Close();

                // Navigate to the album in the library browser
                _libraryBrowserWindow?.NavigateToAlbumByPath(result.AlbumPath);
            }
        }
    }

    public class SongCheckItem
    {
        public string Song { get; set; } = "";
        public bool IsChecked { get; set; }
    }

    public class SequenceItem
    {
        public int Index { get; set; }
        public string Song { get; set; } = "";
    }

    public class TrackSearchResult
    {
        public string TrackTitle { get; set; } = "";
        public string AlbumTitle { get; set; } = "";
        public string AlbumType { get; set; } = "";
        public string AlbumPath { get; set; } = "";
    }
}
