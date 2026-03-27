using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace DeadEditor.Services
{
    /// <summary>
    /// NavigationService - Manages view navigation and data passing in ShellWindow
    /// Supports drill-in navigation (Library → Album → Edit) with data context
    /// </summary>
    public class NavigationService
    {
        private readonly Stack<NavigationEntry> _backStack = new();
        private NavigationEntry? _currentEntry;

        public event EventHandler<NavigationEventArgs>? NavigationRequested;

        /// <summary>
        /// Current view being displayed
        /// </summary>
        public System.Windows.Controls.UserControl? CurrentView => _currentEntry?.View;

        /// <summary>
        /// Current view context data
        /// </summary>
        public object? CurrentContext => _currentEntry?.Context;

        /// <summary>
        /// Can navigate back (stack has entries)
        /// </summary>
        public bool CanGoBack => _backStack.Count > 0;

        /// <summary>
        /// Navigate to a new view with optional context data
        /// </summary>
        public void NavigateTo(System.Windows.Controls.UserControl view, object? context = null)
        {
            // Push current entry to back stack (if exists)
            if (_currentEntry != null)
            {
                _backStack.Push(_currentEntry);
            }

            // Set new current entry
            _currentEntry = new NavigationEntry(view, context);

            // Raise navigation event
            NavigationRequested?.Invoke(this, new NavigationEventArgs(view, context, NavigationType.Forward));
        }

        /// <summary>
        /// Navigate back to previous view
        /// </summary>
        public void GoBack()
        {
            if (!CanGoBack)
            {
                return;
            }

            // Pop previous entry from stack
            var previousEntry = _backStack.Pop();

            // Set as current
            _currentEntry = previousEntry;

            // Raise navigation event
            NavigationRequested?.Invoke(this, new NavigationEventArgs(previousEntry.View, previousEntry.Context, NavigationType.Back));
        }

        /// <summary>
        /// Navigate to a root view, clearing the back stack
        /// </summary>
        public void NavigateToRoot(System.Windows.Controls.UserControl view, object? context = null)
        {
            // Clear back stack
            _backStack.Clear();

            // Set new current entry
            _currentEntry = new NavigationEntry(view, context);

            // Raise navigation event
            NavigationRequested?.Invoke(this, new NavigationEventArgs(view, context, NavigationType.Root));
        }

        /// <summary>
        /// Clear navigation history
        /// </summary>
        public void Clear()
        {
            _backStack.Clear();
            _currentEntry = null;
        }

        private class NavigationEntry
        {
            public System.Windows.Controls.UserControl View { get; }
            public object? Context { get; }

            public NavigationEntry(System.Windows.Controls.UserControl view, object? context)
            {
                View = view;
                Context = context;
            }
        }
    }

    public class NavigationEventArgs : EventArgs
    {
        public System.Windows.Controls.UserControl View { get; }
        public object? Context { get; }
        public NavigationType Type { get; }

        public NavigationEventArgs(System.Windows.Controls.UserControl view, object? context, NavigationType type)
        {
            View = view;
            Context = context;
            Type = type;
        }
    }

    public enum NavigationType
    {
        Forward,
        Back,
        Root
    }
}
