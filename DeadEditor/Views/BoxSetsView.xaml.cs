using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    /// <summary>
    /// Box Sets list view. Reads <see cref="BoxSetDefinition"/>s from disk via
    /// <see cref="BoxSetService.List"/> on every navigation (cheap — dozens of files
    /// at most, per Phase A R3) and renders them in a DataGrid. Shows an empty-state
    /// message when no definitions exist.
    /// </summary>
    public partial class BoxSetsView : System.Windows.Controls.UserControl
    {
        // BoxSetService is instance-based (not a singleton like ConcertLookupService),
        // so we hold one per view. List() returns empty on missing directory or unreadable
        // files, so no try/catch is needed here.
        private readonly BoxSetService _boxSetService = new();

        // ObservableCollection bound once in the ctor so subsequent reloads update the
        // grid without rebinding ItemsSource.
        private readonly ObservableCollection<BoxSetDefinition> _boxSets = new();

        /// <summary>Total number of box-set definitions loaded.</summary>
        public int TotalCount => _boxSets.Count;

        /// <summary>Filtered count — same as TotalCount in MVP (no filter yet).</summary>
        public int FilteredCount => _boxSets.Count;

        /// <summary>Raised when the user activates a saved row (double-click or Enter) to edit it.
        /// Carries the box's slug; the shell reads a fresh copy from disk by that slug and opens
        /// the wizard in edit-mode.</summary>
        public event EventHandler<string>? EditBoxSetRequested;

        public BoxSetsView()
        {
            InitializeComponent();
            BoxSetsDataGrid.ItemsSource = _boxSets;
        }

        /// <summary>Double-click a saved row → request edit. Ignores clicks that land off a row
        /// (header/empty space) where SelectedItem is not a definition.</summary>
        private void BoxSetsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RequestEditForSelectedRow();
        }

        /// <summary>Enter on a selected row → request edit (keyboard parity with double-click).</summary>
        private void BoxSetsDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RequestEditForSelectedRow();
                e.Handled = true;
            }
        }

        private void RequestEditForSelectedRow()
        {
            if (BoxSetsDataGrid.SelectedItem is BoxSetDefinition def)
                EditBoxSetRequested?.Invoke(this, BoxSetService.DeriveSlug(def.Name));
        }

        /// <summary>
        /// Reads all definitions from <c>%APPDATA%/DeadEditor/box-sets/</c> and rebinds
        /// the grid. Called by <c>ShellWindow.NavigateToBoxSets</c> on every navigation.
        /// </summary>
        public void LoadBoxSets()
        {
            _boxSets.Clear();
            foreach (var definition in _boxSetService.List())
            {
                _boxSets.Add(definition);
            }

            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            var isEmpty = _boxSets.Count == 0;
            EmptyStateText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            BoxSetsDataGrid.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
