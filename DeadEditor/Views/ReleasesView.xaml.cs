using DeadEditor.Services;
using System;
using System.Collections.Generic;
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
    /// Releases Editor View — browse, search, add, and manage releases from releases.json.
    /// Two-panel layout: collapsible Series (left) + Standalone releases (right).
    /// </summary>
    public partial class ReleasesView : System.Windows.Controls.UserControl
    {
        private readonly ReleaseLookupService _service;
        private List<SeriesInfo> _seriesData = new();
        private List<string> _standaloneData = new();
        private readonly Dictionary<string, bool> _expandedState = new();

        /// <summary>Returns the total count of all releases.</summary>
        public int ReleaseCount => _service.TotalCount;

        public ReleasesView()
        {
            InitializeComponent();
            _service = ReleaseLookupService.Instance;
        }

        /// <summary>
        /// Loads releases data when the view becomes visible.
        /// Called by ShellWindow on navigation.
        /// </summary>
        public void LoadReleases()
        {
            _seriesData = _service.GetSeriesInfo();
            _standaloneData = _service.GetStandaloneReleases();
            RebuildUI();
        }

        private void RebuildUI()
        {
            var filterText = SearchBox?.Text?.Trim() ?? "";

            RebuildSeriesPanel(filterText);
            RebuildStandalonePanel(filterText);

            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== SERIES PANEL =====

        private void RebuildSeriesPanel(string filterText)
        {
            SeriesPanel.Children.Clear();

            foreach (var series in _seriesData)
            {
                var filteredVolumes = string.IsNullOrEmpty(filterText)
                    ? series.VolumeNames
                    : series.VolumeNames.Where(v => v.Contains(filterText, StringComparison.OrdinalIgnoreCase)).ToList();

                // Show series if name matches or any volume matches
                bool nameMatches = string.IsNullOrEmpty(filterText)
                    || series.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase);

                if (!nameMatches && filteredVolumes.Count == 0)
                    continue;

                var volumesToShow = nameMatches && string.IsNullOrEmpty(filterText) ? series.VolumeNames : filteredVolumes;

                // Series header (clickable to expand/collapse)
                if (!_expandedState.ContainsKey(series.Name))
                    _expandedState[series.Name] = false;

                bool isExpanded = _expandedState[series.Name] || !string.IsNullOrEmpty(filterText);

                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                var arrow = new TextBlock
                {
                    Text = isExpanded ? "\u25BC" : "\u25B6",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Width = 14
                };

                var nameBlock = new TextBlock
                {
                    Text = $"{series.Name}",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var countBlock = new TextBlock
                {
                    Text = $"  ({volumesToShow.Count} volumes)",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    VerticalAlignment = VerticalAlignment.Center
                };

                headerPanel.Children.Add(arrow);
                headerPanel.Children.Add(nameBlock);
                headerPanel.Children.Add(countBlock);

                var headerBorder = new Border
                {
                    Child = headerPanel,
                    Padding = new Thickness(4, 6, 4, 6),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Tag = series.Name
                };

                headerBorder.MouseEnter += (s, e) => headerBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
                headerBorder.MouseLeave += (s, e) => headerBorder.Background = Brushes.Transparent;

                var seriesName = series.Name;
                headerBorder.MouseLeftButtonDown += (s, e) =>
                {
                    _expandedState[seriesName] = !_expandedState[seriesName];
                    RebuildUI();
                    e.Handled = true;
                };

                SeriesPanel.Children.Add(headerBorder);

                // Expanded volume list
                if (isExpanded)
                {
                    var volumePanel = new StackPanel { Margin = new Thickness(22, 0, 0, 8) };

                    foreach (var volumeName in volumesToShow)
                    {
                        var volumeBlock = new TextBlock
                        {
                            Text = volumeName,
                            FontSize = 13,
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
                            Padding = new Thickness(4, 3, 4, 3)
                        };
                        volumePanel.Children.Add(volumeBlock);
                    }

                    // Add/Remove volume buttons (not for complex series like Road Trips)
                    if (!series.IsComplexSeries)
                    {
                        var buttonPanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(4, 6, 0, 0)
                        };

                        var addBtn = new Button
                        {
                            Content = "+ Add Volume",
                            Style = FindResource("DarkButtonStyle") as Style,
                            Margin = new Thickness(0, 0, 8, 0),
                            Tag = seriesName
                        };
                        addBtn.Click += AddVolumeButton_Click;
                        buttonPanel.Children.Add(addBtn);

                        if (volumesToShow.Count > 0)
                        {
                            var removeBtn = new Button
                            {
                                Content = "Remove Last",
                                Style = FindResource("DangerButtonStyle") as Style,
                                Tag = seriesName
                            };
                            removeBtn.Click += RemoveVolumeButton_Click;
                            buttonPanel.Children.Add(removeBtn);
                        }

                        volumePanel.Children.Add(buttonPanel);
                    }

                    SeriesPanel.Children.Add(volumePanel);
                }
            }
        }

        // ===== STANDALONE PANEL =====

        private void RebuildStandalonePanel(string filterText)
        {
            StandalonePanel.Children.Clear();

            var filtered = string.IsNullOrEmpty(filterText)
                ? _standaloneData
                : _standaloneData.Where(r => r.Contains(filterText, StringComparison.OrdinalIgnoreCase)).ToList();

            StandaloneHeader.Text = $"Standalone Releases ({filtered.Count})";

            foreach (var release in filtered)
            {
                var row = CreateStandaloneRow(release);
                StandalonePanel.Children.Add(row);
            }
        }

        private Border CreateStandaloneRow(string release)
        {
            var border = new Border
            {
                Padding = new Thickness(6, 5, 6, 5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = release
            };

            var nameBlock = new TextBlock
            {
                Text = release,
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            border.Child = nameBlock;

            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;

            // Double-click to edit inline
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    StartInlineEdit(border, release);
                    e.Handled = true;
                }
            };

            // Context menu
            var contextMenu = new ContextMenu
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"))
            };

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (s, e) => StartInlineEdit(border, release);
            contextMenu.Items.Add(editItem);

            var removeItem = new MenuItem { Header = "Remove" };
            removeItem.Click += (s, e) => RemoveStandaloneRelease(release);
            contextMenu.Items.Add(removeItem);

            border.ContextMenu = contextMenu;

            return border;
        }

        private void StartInlineEdit(Border container, string release)
        {
            var textBox = new TextBox
            {
                Text = release,
                FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 3, 4, 3),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };

            var originalChild = container.Child;
            container.Child = textBox;
            textBox.Focus();
            textBox.SelectAll();

            void CommitEdit()
            {
                var newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != release)
                {
                    _service.RenameStandaloneRelease(release, newName);
                    _standaloneData = _service.GetStandaloneReleases();
                    RebuildUI();
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

        private void RemoveStandaloneRelease(string release)
        {
            var result = MessageBox.Show(
                $"Remove \"{release}\" from standalone releases?",
                "Remove Release",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _service.RemoveStandaloneRelease(release);
            _standaloneData = _service.GetStandaloneReleases();
            RebuildUI();
        }

        // ===== SERIES ADD/REMOVE =====

        private void AddVolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string seriesName)
            {
                var name = _service.AddVolumeToSeries(seriesName);
                if (name != null)
                {
                    _seriesData = _service.GetSeriesInfo();
                    _expandedState[seriesName] = true;
                    RebuildUI();
                }
            }
        }

        private void RemoveVolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string seriesName)
            {
                var result = MessageBox.Show(
                    $"Remove the last volume from {seriesName}?",
                    "Remove Volume",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                if (_service.RemoveLastVolumeFromSeries(seriesName))
                {
                    _seriesData = _service.GetSeriesInfo();
                    RebuildUI();
                }
            }
        }

        // ===== STANDALONE ADD =====

        private void AddReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            var addPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var textBox = new TextBox
            {
                Width = 280,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                BorderThickness = new Thickness(1),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"))
            };

            var hintBlock = new TextBlock
            {
                Text = "Enter + press Enter",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };

            addPanel.Children.Add(textBox);
            addPanel.Children.Add(hintBlock);

            StandalonePanel.Children.Insert(0, addPanel);
            textBox.Focus();

            void CommitAdd()
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    if (_service.IsKnownRelease(name))
                    {
                        App.Alerts.Notify($"A release named \"{name}\" already exists.", AlertSeverity.Warning, "Duplicate");
                        StandalonePanel.Children.Remove(addPanel);
                        return;
                    }

                    _service.AddRelease(name);
                    _standaloneData = _service.GetStandaloneReleases();
                }

                StandalonePanel.Children.Remove(addPanel);
                RebuildUI();
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
                    StandalonePanel.Children.Remove(addPanel);
                    ev.Handled = true;
                }
            };

            textBox.LostFocus += (s, ev) =>
            {
                if (StandalonePanel.Children.Contains(addPanel))
                    CommitAdd();
            };
        }

        // ===== SEARCH =====

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RebuildUI();
        }
    }
}
