using DeadEditor.Models;
using System;
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

            // Section 1: File Metadata (read fresh from FLAC/MP3 tags on disk)
            AddSectionHeader("File Metadata");
            ReadFileMetadata(track.FilePath);

            // Section 2: Import Status (in-memory pipeline state)
            AddSectionHeader("Import Status");
            AddField("File Path", track.FilePath ?? "");
            AddField("Original Title", track.RawTitle ?? track.Title ?? "");
            AddField("Current Song Name", track.SongName ?? "");
            AddField("Is Matched", track.IsMatched == true ? "Yes" : "No");
            AddField("Is Modified", track.IsModified ? "Yes" : "No");
        }

        private void ReadFileMetadata(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                AddField("Error", "File not found: " + (filePath ?? "(no path)"));
                return;
            }

            try
            {
                using var file = TagLib.File.Create(filePath);

                // Standard tags
                AddField("Filename", System.IO.Path.GetFileName(filePath));
                AddField("Title", file.Tag.Title ?? "");
                AddField("Track Number", file.Tag.Track > 0 ? file.Tag.Track.ToString() : "");
                AddField("Disc Number", file.Tag.Disc > 0 ? file.Tag.Disc.ToString() : "");
                AddField("Artist", file.Tag.FirstPerformer ?? "");
                AddField("Album Artist", file.Tag.FirstAlbumArtist ?? "");
                AddField("Album", file.Tag.Album ?? "");
                AddField("Year", file.Tag.Year > 0 ? file.Tag.Year.ToString() : "");
                AddField("Genre", file.Tag.FirstGenre ?? "");
                AddField("Comment", file.Tag.Comment ?? "");
                AddField("Duration", file.Properties.Duration.ToString(@"mm\:ss"));

                // Custom FLAC Vorbis Comment fields
                if (file.TagTypes.HasFlag(TagLib.TagTypes.Xiph))
                {
                    var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
                    if (xiph != null)
                    {
                        AddField("ALBUMDATE", xiph.GetFirstField("ALBUMDATE") ?? "");
                        AddField("VENUE", xiph.GetFirstField("VENUE") ?? "");
                        AddField("CITYSTATE", xiph.GetFirstField("CITYSTATE") ?? "");
                        AddField("ALBUMNAME", xiph.GetFirstField("ALBUMNAME") ?? "");
                        AddField("ALBUMTYPE", xiph.GetFirstField("ALBUMTYPE") ?? "");
                        AddField("Release MBID", xiph.GetFirstField("MUSICBRAINZ_ALBUMID") ?? "");
                    }
                }
                // Custom MP3 ID3v2 fields
                else if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    var id3v2 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                    if (id3v2 != null)
                    {
                        AddField("ALBUMDATE", GetId3v2TextField(id3v2, "ALBUMDATE"));
                        AddField("VENUE", GetId3v2TextField(id3v2, "VENUE"));
                        AddField("CITYSTATE", GetId3v2TextField(id3v2, "CITYSTATE"));
                        AddField("ALBUMNAME", GetId3v2TextField(id3v2, "ALBUMNAME"));
                        AddField("ALBUMTYPE", GetId3v2TextField(id3v2, "ALBUMTYPE"));
                        AddField("Release MBID", GetId3v2TextField(id3v2, "MusicBrainz Album Id"));
                    }
                }
            }
            catch (Exception ex)
            {
                AddField("Error", $"Could not read file: {ex.Message}");
            }
        }

        private static string GetId3v2TextField(TagLib.Id3v2.Tag tag, string fieldId)
        {
            foreach (var frame in tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                if (string.Equals(frame.Description, fieldId, StringComparison.OrdinalIgnoreCase)
                    && frame.Text.Length > 0)
                {
                    return frame.Text[0] ?? "";
                }
            }
            return "";
        }

        private void AddSectionHeader(string title)
        {
            var header = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x56, 0x9C, 0xD6)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            FieldsPanel.Children.Add(header);

            var separator = new Border
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 4)
            };
            FieldsPanel.Children.Add(separator);
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
