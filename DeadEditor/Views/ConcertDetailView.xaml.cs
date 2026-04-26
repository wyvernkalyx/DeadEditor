using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DeadEditor
{
    public partial class ConcertDetailView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly ConcertReference _concert;
        private readonly List<LibraryShow>? _libraryShows;

        public string ConcertDate => _concert.Date;
        public string VenueName => _concert.Venue;

        public ConcertDetailView(ShellWindow shell, ConcertReference concert, List<LibraryShow>? libraryShows = null)
        {
            InitializeComponent();
            _shell = shell;
            _concert = concert;
            _libraryShows = libraryShows;
        }

        private void ConcertDetailView_Loaded(object sender, RoutedEventArgs e)
        {
            BuildSetlistDisplay();
            BuildLibrarySection();
        }

        private void BuildSetlistDisplay()
        {
            // Left panel metadata
            DateText.Text = _concert.Date;
            VenueText.Text = _concert.Venue;
            LocationText.Text = _concert.FormattedLocation;
            SongCountText.Text = _concert.SongCount == 1 ? "1 song" : $"{_concert.SongCount} songs";

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

        // ===== LIBRARY SECTION =====

        private void BuildLibrarySection()
        {
            LibrarySection.Children.Clear();

            if (_libraryShows == null || _libraryShows.Count == 0)
            {
                // Missing concert — show Import button
                var importButton = new System.Windows.Controls.Button
                {
                    Content = "Import Recording for This Date\u2026",
                    FontSize = 14,
                    Padding = new Thickness(12, 8, 12, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left
                };

                // Style to match dark theme
                importButton.Style = CreateDarkButtonStyle();
                importButton.Click += ImportButton_Click;
                LibrarySection.Children.Add(importButton);
            }
            else
            {
                // Owned concert — show Recordings list
                LibrarySection.Children.Add(new TextBlock
                {
                    Text = _libraryShows.Count == 1 ? "RECORDING" : "RECORDINGS",
                    Foreground = Brush("#888888"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                foreach (var show in _libraryShows)
                {
                    var label = BuildRecordingLabel(show);
                    var link = new TextBlock
                    {
                        Text = label,
                        Foreground = Brush("#4EC9B0"),
                        FontSize = 14,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 2, 0, 2),
                        TextWrapping = TextWrapping.Wrap,
                        Tag = show
                    };
                    link.MouseLeftButtonUp += RecordingLink_Click;

                    // Underline on hover
                    link.MouseEnter += (s, e) =>
                    {
                        if (s is TextBlock tb) tb.TextDecorations = TextDecorations.Underline;
                    };
                    link.MouseLeave += (s, e) =>
                    {
                        if (s is TextBlock tb) tb.TextDecorations = null;
                    };

                    LibrarySection.Children.Add(link);
                }
            }
        }

        private static string BuildRecordingLabel(LibraryShow show)
        {
            if (!string.IsNullOrEmpty(show.OfficialRelease))
                return show.OfficialRelease;
            if (!string.IsNullOrEmpty(show.AlbumName))
                return show.AlbumName;

            var typeLabel = show.Type == AlbumType.OfficialRelease ? "Official" : "Audience";
            return $"{show.Date} ({typeLabel})";
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            _shell.ImportForConcertDate(_concert);
        }

        private void RecordingLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is LibraryShow show)
            {
                var albumView = new AlbumDetailView(_shell, show);
                _shell.Navigation.NavigateTo(albumView, show);
            }
        }

        private Style CreateDarkButtonStyle()
        {
            var buttonType = typeof(System.Windows.Controls.Button);
            var style = new Style(buttonType);
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, Brush("#3E3E42")));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Brush("#E0E0E0")));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, Brush("#555555")));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(1)));

            var template = new ControlTemplate(buttonType);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, Brush("#555555")));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
            return style;
        }

        // ===== EXTERNAL LINKS =====

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
