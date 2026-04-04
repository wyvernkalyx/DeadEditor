using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeadEditor
{
    public partial class ConcertDetailView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly ConcertReference _concert;

        public string ConcertDate => _concert.Date;
        public string VenueName => _concert.Venue;

        public ConcertDetailView(ShellWindow shell, ConcertReference concert)
        {
            InitializeComponent();
            _shell = shell;
            _concert = concert;
        }

        private void ConcertDetailView_Loaded(object sender, RoutedEventArgs e)
        {
            BuildSetlistDisplay();
        }

        private void BuildSetlistDisplay()
        {
            // Left panel metadata
            DateText.Text = _concert.Date;
            VenueText.Text = _concert.Venue;
            LocationText.Text = _concert.FormattedLocation;
            SongCountText.Text = _concert.SongCount == 1 ? "1 song" : $"{_concert.SongCount} songs";

            // Jerrybase link
            if (Regex.IsMatch(_concert.Date, @"^\d{4}-\d{2}-\d{2}$"))
                JerrybaseLink.Visibility = Visibility.Visible;

            // setlist.fm link
            if (!string.IsNullOrEmpty(_concert.SetlistFmUrl))
                SetlistFmLink.Visibility = Visibility.Visible;

            // Build setlist
            SetlistPanel.Children.Clear();

            if (!_concert.HasSetlist || _concert.Sets.Count == 0)
            {
                SetlistPanel.Children.Add(new TextBlock
                {
                    Text = "No setlist data available",
                    Foreground = Brush("#888888"),
                    FontSize = 15,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0)
                });
                return;
            }

            var heady = HeadyVersionService.Instance;
            int headyCount = 0;
            int trackNum = 0;

            foreach (var set in _concert.Sets)
            {
                // Set header — strip trailing colon
                var setName = set.Name.TrimEnd(':', ' ');
                SetlistPanel.Children.Add(new TextBlock
                {
                    Text = setName,
                    Foreground = Brush("#4FC1FF"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, trackNum == 0 ? 0 : 16, 0, 8)
                });

                foreach (var song in set.Songs)
                {
                    trackNum++;
                    var songDate = !string.IsNullOrEmpty(song.Date) ? song.Date : _concert.Date;
                    var segueMarker = song.Segue ? " >" : "";

                    // Heady version lookup
                    var headyMatch = heady.GetHeadyVersion(song.Name, songDate);

                    // Build the song row as a horizontal panel
                    var row = new StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                        Margin = new Thickness(8, 3, 0, 3)
                    };

                    // Track number
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{trackNum}.",
                        Foreground = Brush("#888888"),
                        FontSize = 15,
                        Width = 36,
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 0, 10, 0)
                    });

                    // Heady icon
                    if (headyMatch != null)
                    {
                        headyCount++;
                        var headyIcon = new TextBlock
                        {
                            Text = "\u26A1",
                            Foreground = Brush("#D4A017"),
                            FontSize = 15,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Margin = new Thickness(0, 0, 5, 0),
                            ToolTip = $"#{headyMatch.Rank} — {headyMatch.Votes} votes",
                            Tag = headyMatch.Url
                        };
                        headyIcon.MouseLeftButtonUp += HeadyIcon_Click;
                        row.Children.Add(headyIcon);
                    }

                    // Song title with date
                    var titleBlock = new TextBlock
                    {
                        Text = $"{song.Name}{segueMarker} ({songDate})",
                        Foreground = Brush("#E0E0E0"),
                        FontSize = 15
                    };

                    // Info tooltip (no visual change — just hover text)
                    if (!string.IsNullOrEmpty(song.Info))
                    {
                        titleBlock.ToolTip = song.Info;
                    }

                    row.Children.Add(titleBlock);
                    SetlistPanel.Children.Add(row);
                }
            }

            // Heady count in left panel
            if (headyCount > 0)
            {
                HeadyCountText.Text = headyCount == 1
                    ? "\u26A1 1 heady version"
                    : $"\u26A1 {headyCount} heady versions";
                HeadyCountText.Visibility = Visibility.Visible;
            }
        }

        // ===== EXTERNAL LINKS =====

        private void JerrybaseLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://jerrybase.com/events/{_concert.Date}",
                UseShellExecute = true
            });
        }

        private void SetlistFmLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_concert.SetlistFmUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _concert.SetlistFmUrl,
                    UseShellExecute = true
                });
            }
        }

        private void HeadyIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock tb && tb.Tag is string url && !string.IsNullOrEmpty(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                e.Handled = true;
            }
        }

        public void NavigateBack()
        {
            _shell.Navigation.GoBack();
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
    }
}
