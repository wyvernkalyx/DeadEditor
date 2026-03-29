using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    public partial class PlaylistPanel : System.Windows.Controls.UserControl
    {
        private readonly AudioPlayerService _player;
        private readonly ObservableCollection<PlaylistTrackViewModel> _playlistViewModels;
        private bool _isExpanded = false;
        private const double ExpandedHeight = 200;

        public PlaylistPanel()
        {
            InitializeComponent();

            _player = App.PlaybackService;
            _playlistViewModels = new ObservableCollection<PlaylistTrackViewModel>();
            PlaylistDataGrid.ItemsSource = _playlistViewModels;

            // Subscribe to playlist changes
            _player.Playlist.CollectionChanged += Playlist_CollectionChanged;
            _player.TrackChanged += Player_TrackChanged;

            // Initialize playlist view models from current playlist
            RebuildPlaylistViewModels();

            // Update footer stats
            UpdateTrackCount();

            // Subscribe to unloaded event to cleanup
            Unloaded += PlaylistPanel_Unloaded;

            // Start collapsed
            TrackListBorder.Height = 0;
            ChevronIcon.Text = "▶";
        }

        private void PlaylistPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            // Cleanup when control is unloaded
            _player.Playlist.CollectionChanged -= Playlist_CollectionChanged;
            _player.TrackChanged -= Player_TrackChanged;
        }

        // ===== PLAYLIST SYNCHRONIZATION =====

        private void Playlist_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RebuildPlaylistViewModels();
                UpdateTrackCount();
            });
        }

        private void RebuildPlaylistViewModels()
        {
            _playlistViewModels.Clear();

            foreach (var track in _player.Playlist)
            {
                _playlistViewModels.Add(new PlaylistTrackViewModel(track, _player));
            }
        }

        private void Player_TrackChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Refresh IsCurrentTrack for all rows
                foreach (var vm in _playlistViewModels)
                {
                    vm.RefreshIsCurrentTrack();
                }
            });
        }

        private void UpdateTrackCount()
        {
            var count = _player.Playlist.Count;
            TrackCountText.Text = count == 1 ? "1 track" : $"{count} tracks";
        }

        // ===== PLAYLIST ACTIONS =====

        private void PlaylistDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PlaylistDataGrid.SelectedItem is PlaylistTrackViewModel vm)
            {
                if (_player.Playlist.Count == 0 || !_player.Playlist.Contains(vm.Track))
                {
                    return;
                }

                _player.Play(vm.Track);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _player.Playlist.Clear();
        }

        /// <summary>
        /// Removes the selected track from the playlist. Returns true if a track was removed.
        /// Called by ShellWindow for the Delete keyboard shortcut.
        /// </summary>
        public bool TryRemoveSelectedTrack()
        {
            if (PlaylistDataGrid.SelectedItem is PlaylistTrackViewModel vm)
            {
                _player.Playlist.Remove(vm.Track);
                return true;
            }
            return false;
        }

        private void Header_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle playlist expansion
            _isExpanded = !_isExpanded;

            if (_isExpanded)
            {
                TrackListBorder.Height = ExpandedHeight;
                ChevronIcon.Text = "▼";
            }
            else
            {
                TrackListBorder.Height = 0;
                ChevronIcon.Text = "▶";
            }
        }
    }
}
