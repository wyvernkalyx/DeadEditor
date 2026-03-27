using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    public partial class LibraryGridView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibrarySettings _settings;
        private List<LibraryShow> _shows = new();

        public int ConcertCount => _shows.Count;

        public LibraryGridView(ShellWindow shell)
        {
            InitializeComponent();
            _shell = shell;
            _settings = LibrarySettings.Load();
        }

        private void LibraryGridView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadShows();
        }

        private void LoadShows()
        {
            _shows.Clear();

            // Load audience recordings
            if (!string.IsNullOrEmpty(_settings.LibraryRootPath) && Directory.Exists(_settings.LibraryRootPath))
            {
                LoadAudienceRecordings();
            }

            // Load official releases
            if (!string.IsNullOrEmpty(_settings.OfficialReleasesPath) && Directory.Exists(_settings.OfficialReleasesPath))
            {
                LoadOfficialReleases();
            }

            // Sort by type, then date/name
            _shows = _shows.OrderBy(s => s.Type).ThenByDescending(s => s.Date).ToList();

            ShowsDataGrid.ItemsSource = _shows;
        }

        private void LoadAudienceRecordings()
        {
            var topFolders = Directory.GetDirectories(_settings.LibraryRootPath);

            foreach (var yearFolder in topFolders)
            {
                var yearName = Path.GetFileName(yearFolder);
                if (!System.Text.RegularExpressions.Regex.IsMatch(yearName, @"^\d{4}$"))
                    continue;

                var showFolders = Directory.GetDirectories(yearFolder);

                foreach (var showFolder in showFolders)
                {
                    var folderName = Path.GetFileName(showFolder);
                    var parts = folderName.Split(new[] { " - " }, StringSplitOptions.None);

                    if (parts.Length >= 2)
                    {
                        string date = parts[0];
                        string venue = "";
                        string city = "";
                        string state = "";

                        if (parts.Length == 3)
                        {
                            venue = parts[1];
                            var locationParts = parts[2].Split(new[] { ", " }, StringSplitOptions.None);
                            city = locationParts.Length > 0 ? locationParts[0] : "";
                            state = locationParts.Length > 1 ? locationParts[1] : "";
                        }
                        else if (parts.Length == 2)
                        {
                            var venueParts = parts[1].Split(new[] { ", " }, StringSplitOptions.None);
                            venue = venueParts.Length > 0 ? venueParts[0] : "";
                            city = venueParts.Length > 1 ? venueParts[1] : "";
                            state = venueParts.Length > 2 ? venueParts[2] : "";
                        }

                        var audioFiles = Directory.GetFiles(showFolder, "*.flac").Concat(Directory.GetFiles(showFolder, "*.mp3")).ToArray();

                        _shows.Add(new LibraryShow
                        {
                            Type = AlbumType.AudienceRecording,
                            Date = date,
                            Venue = venue,
                            City = city,
                            State = state,
                            Location = !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state) ? $"{city}, {state}" : city + state,
                            TrackCount = audioFiles.Length,
                            FolderPath = showFolder
                        });
                    }
                }
            }
        }

        private void LoadOfficialReleases()
        {
            // Load from Studio Albums folder
            var studioPath = Path.Combine(_settings.OfficialReleasesPath, "Studio Albums");
            if (Directory.Exists(studioPath))
            {
                foreach (var albumFolder in Directory.GetDirectories(studioPath))
                {
                    var folderName = Path.GetFileName(albumFolder);
                    var audioFiles = Directory.GetFiles(albumFolder, "*.flac").Concat(Directory.GetFiles(albumFolder, "*.mp3")).ToArray();

                    string albumName = folderName;
                    int? year = null;

                    var yearMatch = System.Text.RegularExpressions.Regex.Match(folderName, @"^(.+?)\s*\((\d{4})\)");
                    if (yearMatch.Success)
                    {
                        albumName = yearMatch.Groups[1].Value.Trim();
                        if (int.TryParse(yearMatch.Groups[2].Value, out var y))
                            year = y;
                    }

                    _shows.Add(new LibraryShow
                    {
                        Type = AlbumType.OfficialRelease,
                        AlbumName = albumName,
                        ReleaseYear = year,
                        TrackCount = audioFiles.Length,
                        FolderPath = albumFolder
                    });
                }
            }

            // Load from series folders (Dave's Picks, etc.)
            var seriesFolders = Directory.GetDirectories(_settings.OfficialReleasesPath).Where(f => !Path.GetFileName(f).Equals("Studio Albums", StringComparison.OrdinalIgnoreCase));

            foreach (var seriesFolder in seriesFolders)
            {
                foreach (var releaseFolder in Directory.GetDirectories(seriesFolder))
                {
                    var folderName = Path.GetFileName(releaseFolder);
                    var audioFiles = Directory.GetFiles(releaseFolder, "*.flac").Concat(Directory.GetFiles(releaseFolder, "*.mp3")).ToArray();

                    if (audioFiles.Length == 0) continue;

                    _shows.Add(new LibraryShow
                    {
                        Type = AlbumType.OfficialRelease,
                        OfficialRelease = folderName,
                        TrackCount = audioFiles.Length,
                        FolderPath = releaseFolder
                    });
                }
            }
        }

        private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ShowsDataGrid.SelectedItem is LibraryShow show)
            {
                // Navigate to album detail view
                var albumView = new AlbumDetailView(_shell, show);
                _shell.Navigation.NavigateTo(albumView, show);
            }
        }
    }
}
