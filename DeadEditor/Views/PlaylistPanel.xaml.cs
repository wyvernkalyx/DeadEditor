using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        private void PlaylistDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(PlaylistDataGrid, e.GetPosition(PlaylistDataGrid));
            if (hit == null) return;

            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not DataGridRow row) return;
            if (row.Item is not PlaylistTrackViewModel vm) return;

            var clickedTrack = vm.Track;

            var menu = new ContextMenu
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2)
            };

            var menuItemStyle = new Style(typeof(MenuItem));
            menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0))));
            menuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 6, 20, 6)));
            menuItemStyle.Setters.Add(new Setter(MenuItem.FontSizeProperty, 14.0));

            var playNowItem = new MenuItem { Header = "▶  Play Now", Style = menuItemStyle };
            playNowItem.Click += (s, args) => _player.Play(clickedTrack);
            menu.Items.Add(playNowItem);

            var trackInfoItem = new MenuItem { Header = "\U0001F4C4  Track Info", Style = menuItemStyle };
            trackInfoItem.Click += (s, args) =>
            {
                var dialog = new TrackInfoDialog(clickedTrack);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            };
            menu.Items.Add(trackInfoItem);

            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))
            });

            var removeItem = new MenuItem { Header = "🗑  Remove from Playlist", Style = menuItemStyle };
            removeItem.Click += (s, args) => _player.Playlist.Remove(clickedTrack);
            menu.Items.Add(removeItem);

            menu.IsOpen = true;
            e.Handled = true;
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
