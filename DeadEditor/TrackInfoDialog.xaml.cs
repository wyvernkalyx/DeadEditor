using DeadEditor.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeadEditor
{
    public partial class TrackInfoDialog : Window
    {
        public TrackInfoDialog(TrackInfo track, string? artist = null, string? album = null)
        {
            InitializeComponent();

            AddField("Filename", track.FileName ?? "");
            AddField("File Path", track.FilePath ?? "");
            AddField("Track Number", track.DisplayTrackNumber);
            AddField("Original Title", track.RawTitle ?? track.Title ?? "");
            AddField("Song Name", track.SongName ?? "");
            AddField("Artist", artist ?? "");
            AddField("Album", album ?? "");
            AddField("Date", track.TrackDate ?? track.AlbumDate ?? "");
            AddField("Duration", track.Duration ?? "");
            AddField("Segue", track.Segue ? "Yes >" : "No");
            AddField("Matched", track.IsMatched == true ? "Yes" : "No");
        }

        private void AddField(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "\u2014" : value,
                Foreground = new SolidColorBrush(
                    string.IsNullOrEmpty(value)
                        ? System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)
                        : System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);

            if (!string.IsNullOrEmpty(value))
            {
                var copyBtn = new System.Windows.Controls.Button
                {
                    Content = "\U0001F4CB",
                    Style = (Style)FindResource("CopyButtonStyle"),
                    Tag = value
                };
                copyBtn.Click += CopyButton_Click;
                Grid.SetColumn(copyBtn, 2);
                row.Children.Add(copyBtn);
            }

            FieldsPanel.Children.Add(row);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string value)
            {
                System.Windows.Clipboard.SetText(value);

                // Brief visual feedback: show checkmark, revert after 1.5s
                var original = btn.Content;
                btn.Content = "\u2713";
                btn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0));

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    btn.Content = original;
                    btn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
                };
                timer.Start();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
