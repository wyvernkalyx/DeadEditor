using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DeadEditor
{
    public partial class ConcertDatabaseView : System.Windows.Controls.UserControl
    {
        private List<ConcertReference> _allConcerts = new();
        private List<ConcertReference> _filteredConcerts = new();
        private bool _isLoaded;

        // Cross-reference: dates the user has in their library
        private HashSet<string> _libraryDates = new();

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
        /// Sets the library dates for cross-referencing "In Library" status.
        /// Called by ShellWindow after the library is loaded.
        /// </summary>
        public void SetLibraryDates(IEnumerable<string> dates)
        {
            _libraryDates = new HashSet<string>(dates);
        }

        /// <summary>
        /// Apply a search filter across date, venue, city, state, and song names.
        /// </summary>
        public void ApplyFilter(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredConcerts = _allConcerts;
            }
            else
            {
                var query = searchText.Trim();
                _filteredConcerts = _allConcerts.Where(c =>
                    c.Date.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Venue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.City.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.State.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Tracks.Any(t => t.SongName.Contains(query, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            ConcertsDataGrid.ItemsSource = _filteredConcerts;
        }

        private void ConcertsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ConcertsDataGrid.SelectedItem is not ConcertReference concert)
                return;

            // Navigate to full-screen Concert Detail view
            var shell = Window.GetWindow(this) as ShellWindow;
            shell?.NavigateToConcertDetail(concert);
        }
    }
}
