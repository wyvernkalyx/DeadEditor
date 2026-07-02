using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free formatter for a combined run's display label, e.g.
    /// "Dark Star &gt; St. Stephen &gt; The Eleven (7–9)". Shared by the setlist editor's combine
    /// list (<c>EditSetlistView</c>) and the read-only concert detail view (<c>ConcertDetailView</c>)
    /// so both render an alias entry identically. The caller supplies the index→name resolution
    /// (edit view reads its live tracks; detail view reads the flattened concert setlist), keeping
    /// this helper free of any view state and unit-testable in isolation.
    /// </summary>
    public static class CombineLabel
    {
        /// <summary>
        /// Renders a covered run as "Name &gt; Name (first–last)" where the positions are the
        /// 1-based flattened official positions (indices + 1) and names come from
        /// <paramref name="nameAt"/>. Empty/null runs render "(empty)". The range separator is an
        /// en dash (U+2013). <paramref name="nameAt"/> resolves a 0-based covered index to a song
        /// name; it should return a sensible fallback (e.g. "#N") for an out-of-range index.
        /// </summary>
        public static string Describe(IReadOnlyList<int> coveredIndices, Func<int, string> nameAt)
        {
            if (coveredIndices == null || coveredIndices.Count == 0) return "(empty)";

            var names = coveredIndices.Select(nameAt);
            var range = $"{coveredIndices.Min() + 1}–{coveredIndices.Max() + 1}";
            return $"{string.Join(" > ", names)} ({range})";
        }
    }
}
