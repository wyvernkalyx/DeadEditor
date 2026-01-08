using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor
{
    public partial class ManageSongsDialog : Window
    {
        private readonly NormalizationService _normalizationService;
        private List<SongDisplayItem> _allSongs = new();
        private List<SongDisplayItem> _filteredSongs = new();

        public ManageSongsDialog(NormalizationService normalizationService)
        {
            InitializeComponent();
            _normalizationService = normalizationService;
            LoadSongs();
        }

        private void LoadSongs()
        {
            _allSongs.Clear();

            // Get all artists
            var artists = _normalizationService.GetAllArtists();

            // If no artists in new structure, check legacy
            if (!artists.Any())
            {
                // Load from legacy Songs structure
                var allTitles = _normalizationService.GetAllTitles();
                foreach (var title in allTitles)
                {
                    _allSongs.Add(new SongDisplayItem
                    {
                        Title = title,
                        Artist = "Unknown",
                        Aliases = new List<string>(),
                        AliasesDisplay = "No aliases",
                        HasAliases = Visibility.Collapsed
                    });
                }
            }
            else
            {
                // Load from artist-based structure
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var database = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.SongDatabase>(json);

                    if (database?.Artists != null)
                    {
                        foreach (var artist in database.Artists)
                        {
                            foreach (var song in artist.Songs ?? new List<Models.SongEntry>())
                            {
                                var aliases = song.Aliases ?? new List<string>();
                                var hasAliases = aliases.Any();

                                _allSongs.Add(new SongDisplayItem
                                {
                                    Title = song.OfficialTitle,
                                    Artist = artist.Name,
                                    Aliases = aliases,
                                    AliasesDisplay = hasAliases
                                        ? $"Aliases: {string.Join(", ", aliases)}"
                                        : "No aliases",
                                    HasAliases = hasAliases ? Visibility.Visible : Visibility.Collapsed
                                });
                            }
                        }
                    }
                }
            }

            // Sort by title
            _allSongs = _allSongs.OrderBy(s => s.Title).ToList();

            // Populate artist filter
            PopulateArtistFilter();

            // Update statistics
            UpdateStatistics();

            // Show all songs initially
            ApplyFilter();
        }

        private void PopulateArtistFilter()
        {
            var artists = _allSongs.Select(s => s.Artist).Distinct().OrderBy(a => a).ToList();
            artists.Insert(0, "All Artists");

            ArtistFilterComboBox.ItemsSource = artists;
            ArtistFilterComboBox.SelectedIndex = 0;
        }

        private void UpdateStatistics()
        {
            var totalSongs = _allSongs.Count;
            var artistCount = _allSongs.Select(s => s.Artist).Distinct().Count();
            var songsWithAliases = _allSongs.Count(s => s.Aliases.Any());

            StatsTextBlock.Text = $"Total: {totalSongs} songs | Artists: {artistCount} | Songs with aliases: {songsWithAliases}";
        }

        private void ApplyFilter()
        {
            var searchText = SearchTextBox.Text?.ToLowerInvariant() ?? "";
            var selectedArtist = ArtistFilterComboBox.SelectedItem as string;

            _filteredSongs = _allSongs.Where(s =>
            {
                // Artist filter
                bool artistMatch = selectedArtist == "All Artists" || s.Artist == selectedArtist;

                // Search filter (searches title and aliases)
                bool searchMatch = string.IsNullOrWhiteSpace(searchText) ||
                                   s.Title.ToLowerInvariant().Contains(searchText) ||
                                   s.Aliases.Any(a => a.ToLowerInvariant().Contains(searchText));

                return artistMatch && searchMatch;
            }).ToList();

            SongsItemsControl.ItemsSource = _filteredSongs;

            // Update result count
            if (_filteredSongs.Count != _allSongs.Count)
            {
                ResultCountTextBlock.Text = $"Showing {_filteredSongs.Count} of {_allSongs.Count} songs";
            }
            else
            {
                ResultCountTextBlock.Text = "";
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ArtistFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "song_database",
                DefaultExt = ".txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(dialog.FileName))
                    {
                        writer.WriteLine("SONG DATABASE EXPORT");
                        writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"Total Songs: {_allSongs.Count}");
                        writer.WriteLine();
                        writer.WriteLine(new string('=', 80));
                        writer.WriteLine();

                        var groupedByArtist = _filteredSongs.GroupBy(s => s.Artist).OrderBy(g => g.Key);

                        foreach (var artistGroup in groupedByArtist)
                        {
                            writer.WriteLine($"ARTIST: {artistGroup.Key}");
                            writer.WriteLine(new string('-', 80));
                            writer.WriteLine();

                            foreach (var song in artistGroup.OrderBy(s => s.Title))
                            {
                                writer.WriteLine($"  {song.Title}");
                                if (song.Aliases.Any())
                                {
                                    writer.WriteLine($"    Aliases: {string.Join(", ", song.Aliases)}");
                                }
                                writer.WriteLine();
                            }

                            writer.WriteLine();
                        }
                    }

                    MessageBox.Show($"Song database exported to:\n{dialog.FileName}",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting database: {ex.Message}",
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    // Helper class for displaying songs in the UI
    public class SongDisplayItem
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public List<string> Aliases { get; set; } = new();
        public string AliasesDisplay { get; set; } = "";
        public Visibility HasAliases { get; set; } = Visibility.Collapsed;
    }
}
