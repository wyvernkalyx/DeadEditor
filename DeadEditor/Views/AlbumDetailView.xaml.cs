using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    public partial class AlbumDetailView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibraryShow _show;
        private readonly MetadataService _metadataService;
        private List<TrackInfo> _tracks = new();

        public string AlbumName => _show.Type == AlbumType.OfficialRelease ? _show.AlbumName : _show.Venue;

        public AlbumDetailView(ShellWindow shell, LibraryShow show)
        {
            InitializeComponent();
            _shell = shell;
            _show = show;
            _metadataService = new MetadataService();
        }

        private void AlbumDetailView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAlbumData();
            LoadTracks();
        }

        private void LoadAlbumData()
        {
            // Set metadata labels
            if (_show.Type == AlbumType.OfficialRelease)
            {
                VenueText.Text = _show.AlbumName;
                LocationText.Text = _show.ReleaseYear.HasValue ? $"Released {_show.ReleaseYear}" : "";
                DateText.Text = "";
            }
            else
            {
                VenueText.Text = _show.Venue;
                LocationText.Text = _show.Location;
                DateText.Text = _show.Date;
            }
        }

        private void LoadTracks()
        {
            _tracks.Clear();

            // Use FolderPaths (plural) to support multi-folder albums
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                var audioFiles = Directory.GetFiles(folder, "*.flac")
                    .Concat(Directory.GetFiles(folder, "*.mp3"))
                    .OrderBy(f => f)
                    .ToList();

                foreach (var file in audioFiles)
                {
                    try
                    {
                        using (var tagFile = TagLib.File.Create(file))
                        {
                            var track = new TrackInfo
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                TrackNumber = tagFile.Tag.Track > 0 ? (int)tagFile.Tag.Track : _tracks.Count + 1,
                                Title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file),
                                Duration = tagFile.Properties.Duration.ToString(@"m\:ss")
                            };

                            _tracks.Add(track);
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }

            TracksDataGrid.ItemsSource = _tracks;
            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";
        }

        private void TracksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TracksDataGrid.SelectedItem is TrackInfo track)
            {
                // Add track to playlist and play
                var playlist = App.PlaybackService.Playlist;

                // Check if already in playlist
                if (!playlist.Contains(track))
                {
                    playlist.Add(track);
                }

                // Play the track
                App.PlaybackService.Play(track);
            }
        }

        public void NavigateBack()
        {
            _shell.Navigation.GoBack();
        }
    }
}
