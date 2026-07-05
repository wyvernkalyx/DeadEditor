namespace DeadEditor.Helpers
{
    /// <summary>
    /// Resolves the "current track" from a <c>DataGrid</c>'s candidate sources, tolerant of both
    /// full-row and cell selection. A full-row grid populates <c>SelectedItem</c>; a cell-selection
    /// grid (<c>SelectionUnit=CellOrRowHeader</c>) leaves <c>SelectedItem</c> null on a bare cell
    /// click and does not drive <c>CurrentItem</c> (which follows row selection/navigation), but it
    /// DOES set <c>CurrentCell.Item</c>. The panel-assign handler (reference-side-panel-spec.md §7.5)
    /// has no click position to hit-test with, so it feeds these candidates here.
    ///
    /// Returns the first candidate that is a <see cref="TrackInfoViewModel"/>, else null. Non-VM
    /// sentinels (null, <c>DependencyProperty.UnsetValue</c>, unrelated objects) are skipped, so the
    /// caller can pass raw grid properties without pre-filtering. WPF-free and unit-testable.
    /// </summary>
    public static class SelectedTrackResolver
    {
        public static TrackInfoViewModel? Resolve(params object?[] candidates)
        {
            if (candidates == null)
                return null;

            foreach (var candidate in candidates)
            {
                if (candidate is TrackInfoViewModel vm)
                    return vm;
            }

            return null;
        }
    }
}
