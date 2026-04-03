using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace DeadEditor
{
    /// <summary>
    /// Songs Editor View — browse, search, add, edit, and remove songs from songs.json.
    /// Displays all songs alphabetically grouped by first letter.
    /// </summary>
    public partial class SongsView : System.Windows.Controls.UserControl
    {
        private SongDatabase? _database;
        private readonly string _databasePath;
        private List<SongEntry> _allSongs = new();
        private string _currentArtist = "";

        /// <summary>
        /// Returns the total number of songs in the database.
        /// </summary>
        public int SongCount => _allSongs.Count;

        public SongsView()
        {
            InitializeComponent();
            _databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
        }

        /// <summary>
        /// Loads songs from the database when the view becomes visible.
        /// Called by ShellWindow on navigation.
        /// </summary>
        public void LoadSongs()
        {
            LoadDatabase();
            RebuildSongList();
        }

        private void LoadDatabase()
        {
            if (!File.Exists(_databasePath))
            {
                _database = new SongDatabase { Songs = new List<SongEntry>(), Artists = new List<ArtistEntry>() };
                return;
            }

            var json = File.ReadAllText(_databasePath);
            _database = JsonConvert.DeserializeObject<SongDatabase>(json);

            // Collect all songs from artist-based structure
            _allSongs = new List<SongEntry>();
            if (_database?.Artists != null && _database.Artists.Count > 0)
            {
                _currentArtist = _database.Artists[0].Name;
                foreach (var artist in _database.Artists)
                {
                    if (artist.Songs != null)
                        _allSongs.AddRange(artist.Songs);
                }
            }

            // Also include legacy songs
            if (_database?.Songs != null)
            {
                _allSongs.AddRange(_database.Songs);
            }

            _allSongs = _allSongs
                .GroupBy(s => s.OfficialTitle, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.OfficialTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void RebuildSongList()
        {
            SongListPanel.Children.Clear();

            var filterText = SearchBox.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(filterText)
                ? _allSongs
                : _allSongs.Where(s => s.OfficialTitle.Contains(filterText, StringComparison.OrdinalIgnoreCase)).ToList();

            // Update placeholder visibility
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            string currentLetter = "";

            foreach (var song in filtered)
            {
                var firstChar = string.IsNullOrEmpty(song.OfficialTitle) ? "#"
                    : char.IsLetter(song.OfficialTitle[0]) ? song.OfficialTitle[0].ToString().ToUpper() : "#";

                // Add letter header if new group
                if (firstChar != currentLetter)
                {
                    currentLetter = firstChar;
                    var header = new TextBlock
                    {
                        Text = currentLetter,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                        Margin = new Thickness(0, currentLetter == filtered.First().OfficialTitle[0].ToString().ToUpper() ? 0 : 16, 0, 6)
                    };
                    SongListPanel.Children.Add(header);
                }

                // Song row
                var row = CreateSongRow(song);
                SongListPanel.Children.Add(row);
            }
        }

        private Border CreateSongRow(SongEntry song)
        {
            var border = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = song
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Song name
            var nameBlock = new TextBlock
            {
                Text = song.OfficialTitle,
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameBlock, 0);
            grid.Children.Add(nameBlock);

            // Alias count (if any)
            if (song.Aliases != null && song.Aliases.Count > 0)
            {
                var aliasBlock = new TextBlock
                {
                    Text = $"{song.Aliases.Count} alias{(song.Aliases.Count == 1 ? "" : "es")}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(aliasBlock, 1);
                grid.Children.Add(aliasBlock);
            }

            border.Child = grid;

            // Hover effect
            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;

            // Double-click to edit
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    StartInlineEdit(border, song);
                    e.Handled = true;
                }
            };

            // Right-click context menu
            var contextMenu = new ContextMenu
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"))
            };

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (s, e) => StartInlineEdit(border, song);
            contextMenu.Items.Add(editItem);

            var removeItem = new MenuItem { Header = "Remove" };
            removeItem.Click += (s, e) => RemoveSong(song);
            contextMenu.Items.Add(removeItem);

            border.ContextMenu = contextMenu;

            return border;
        }

        private void StartInlineEdit(Border container, SongEntry song)
        {
            var textBox = new TextBox
            {
                Text = song.OfficialTitle,
                FontSize = 14,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };

            var originalChild = container.Child;
            container.Child = textBox;
            textBox.Focus();
            textBox.SelectAll();

            void CommitEdit()
            {
                var newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != song.OfficialTitle)
                {
                    // Check for duplicates
                    if (_allSongs.Any(s => s != song && s.OfficialTitle.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show($"A song named \"{newName}\" already exists.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Warning);
                        container.Child = originalChild;
                        return;
                    }

                    song.OfficialTitle = newName;
                    SaveDatabase();
                    LoadDatabase();
                    RebuildSongList();
                }
                else
                {
                    container.Child = originalChild;
                }
            }

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitEdit();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    container.Child = originalChild;
                    e.Handled = true;
                }
            };

            textBox.LostFocus += (s, e) => CommitEdit();
        }

        private void RemoveSong(SongEntry song)
        {
            var result = MessageBox.Show(
                $"Remove \"{song.OfficialTitle}\" from the song database?",
                "Remove Song",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Remove from all artists
            if (_database?.Artists != null)
            {
                foreach (var artist in _database.Artists)
                {
                    artist.Songs?.RemoveAll(s => s.OfficialTitle.Equals(song.OfficialTitle, StringComparison.OrdinalIgnoreCase));
                }
            }

            // Remove from legacy list
            _database?.Songs?.RemoveAll(s => s.OfficialTitle.Equals(song.OfficialTitle, StringComparison.OrdinalIgnoreCase));

            SaveDatabase();
            LoadDatabase();
            RebuildSongList();
        }

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            // Show inline add field at top of list
            var addPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var textBox = new TextBox
            {
                Width = 350,
                FontSize = 14,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                BorderThickness = new Thickness(1),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };

            var hintBlock = new TextBlock
            {
                Text = "Type song name and press Enter",
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            addPanel.Children.Add(textBox);
            addPanel.Children.Add(hintBlock);

            SongListPanel.Children.Insert(0, addPanel);
            textBox.Focus();

            void CommitAdd()
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    if (_allSongs.Any(s => s.OfficialTitle.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show($"A song named \"{name}\" already exists.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SongListPanel.Children.Remove(addPanel);
                        return;
                    }

                    // Add to first artist (or create default)
                    if (_database != null)
                    {
                        if (_database.Artists == null || _database.Artists.Count == 0)
                        {
                            _database.Artists = new List<ArtistEntry>
                            {
                                new ArtistEntry { Name = _currentArtist ?? "Unknown", Songs = new List<SongEntry>() }
                            };
                        }

                        _database.Artists[0].Songs ??= new List<SongEntry>();
                        _database.Artists[0].Songs.Add(new SongEntry
                        {
                            OfficialTitle = name,
                            Aliases = new List<string>()
                        });

                        SaveDatabase();
                        LoadDatabase();
                    }
                }

                SongListPanel.Children.Remove(addPanel);
                RebuildSongList();
            }

            textBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Key.Enter)
                {
                    CommitAdd();
                    ev.Handled = true;
                }
                else if (ev.Key == Key.Escape)
                {
                    SongListPanel.Children.Remove(addPanel);
                    ev.Handled = true;
                }
            };

            textBox.LostFocus += (s, ev) =>
            {
                if (SongListPanel.Children.Contains(addPanel))
                    CommitAdd();
            };
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RebuildSongList();
        }

        private void SaveDatabase()
        {
            if (_database == null) return;

            var json = JsonConvert.SerializeObject(_database, Formatting.Indented);
            var tempPath = _databasePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _databasePath, overwrite: true);
        }
    }
}
