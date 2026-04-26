using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeadEditor
{
    public partial class ConcertDatabaseView : System.Windows.Controls.UserControl
    {
        private List<ConcertReference> _allConcerts = new();
        private List<ConcertReference> _filteredConcerts = new();
        private bool _isLoaded;

        // Cross-reference: dates the user has in their library
        private HashSet<string> _libraryDates = new();
        private Dictionary<string, List<LibraryShow>> _libraryShowsByDate = new();

        // Ownership filter: "All", "Owned", "Missing"
        private string _ownershipFilter = "All";
        private string _lastSearchText = "";

        // Brushes for owned/missing row coloring
        private static readonly SolidColorBrush OwnedBrush = new(System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0)); // #4EC9B0
        private static readonly SolidColorBrush MissingBrush = new(System.Windows.Media.Color.FromRgb(0x6E, 0x6E, 0x6E)); // #6E6E6E

        public int TotalCount => _allConcerts.Count;
        public int FilteredCount => _filteredConcerts.Count;

        public ConcertDatabaseView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Loads concert data from ConcertLookupService. Safe to call multiple times —
        /// only loads on first call.
        /// </summary>
        public void LoadConcerts()
        {
            if (_isLoaded) return;
            _isLoaded = true;

            var service = ConcertLookupService.Instance;
            _allConcerts = service.GetAllConcerts().ToList();
            _filteredConcerts = _allConcerts;
            ConcertsDataGrid.ItemsSource = _filteredConcerts;
        }

        /// <summary>
        /// Sets the library shows for cross-referencing ownership status.
        /// Called by ShellWindow after the library is loaded and after each import.
        /// </summary>
        public void SetLibraryShows(Dictionary<string, List<LibraryShow>> showsByDate)
        {
            _libraryShowsByDate = showsByDate;
            _libraryDates = new HashSet<string>(showsByDate.Keys);

            // Re-apply current filter to update row visibility
            ApplyFilter(_lastSearchText);

            // Force row re-render for color updates
            ConcertsDataGrid.Items.Refresh();
        }

        /// <summary>
        /// Sets the library dates for cross-referencing "In Library" status.
        /// Called by ShellWindow after the library is loaded.
        /// </summary>
        public void SetLibraryDates(IEnumerable<string> dates)
        {
            _libraryDates = new HashSet<string>(dates);
        }

        /// <summary>
        /// Sets the ownership filter and re-applies the combined filter.
        /// </summary>
        public void SetOwnershipFilter(string filter)
        {
            _ownershipFilter = filter;
            ApplyFilter(_lastSearchText);
        }

        /// <summary>
        /// Resets the ownership filter to "All". Called when navigating away from Concerts view.
        /// </summary>
        public void ResetOwnershipFilter()
        {
            _ownershipFilter = "All";
        }

        /// <summary>
        /// Apply a search filter across date, venue, city, state, and song names.
        /// Composes with the ownership filter (AND).
        /// </summary>
        public void ApplyFilter(string searchText)
        {
            _lastSearchText = searchText;

            IEnumerable<ConcertReference> results = _allConcerts;

            // Apply ownership filter
            if (_ownershipFilter == "Owned")
            {
                results = results.Where(c => _libraryDates.Contains(c.Date));
            }
            else if (_ownershipFilter == "Missing")
            {
                results = results.Where(c => !_libraryDates.Contains(c.Date));
            }

            // Apply search text filter
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var query = searchText.Trim();
                results = results.Where(c =>
                    c.Date.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Venue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.City.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.State.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Tracks.Any(t => t.SongName.Contains(query, StringComparison.OrdinalIgnoreCase))
                );
            }

            _filteredConcerts = results.ToList();
            ConcertsDataGrid.ItemsSource = _filteredConcerts;
        }

        // ===== ROW STYLING =====

        private void ConcertsDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is ConcertReference concert)
            {
                bool isOwned = _libraryDates.Contains(concert.Date);
                e.Row.Foreground = isOwned ? OwnedBrush : MissingBrush;
            }
        }

        // ===== CLICK HANDLERS =====

        private void ConcertsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ConcertsDataGrid.SelectedItem is not ConcertReference concert)
                return;

            // Always navigate to Concert Detail view, passing library shows if available
            var shell = Window.GetWindow(this) as ShellWindow;
            var libraryShows = _libraryShowsByDate.GetValueOrDefault(concert.Date);
            shell?.NavigateToConcertDetail(concert, libraryShows);
        }

        // ===== RIGHT-CLICK CONTEXT MENU =====

        private void ConcertsDataGrid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the DataGridRow under the cursor
            var hit = VisualTreeHelper.HitTest(ConcertsDataGrid, e.GetPosition(ConcertsDataGrid));
            if (hit == null) return;

            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not DataGridRow row) return;
            if (row.Item is not ConcertReference concert) return;

            // Build dark-themed context menu
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

            var importItem = new MenuItem
            {
                Header = "Import recording for this date\u2026",
                Style = menuItemStyle
            };
            importItem.Click += (s, args) =>
            {
                var shell = Window.GetWindow(this) as ShellWindow;
                shell?.ImportForConcertDate(concert);
            };
            menu.Items.Add(importItem);

            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
