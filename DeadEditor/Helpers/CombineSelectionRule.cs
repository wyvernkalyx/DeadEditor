using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// One selected setlist-editor row's identity for combine-authoring validity: its 1-based
    /// flattened official <see cref="Position"/> (the editor's "#" column) and its <see cref="Set"/>
    /// label. A WPF-free input record so the rule below is unit-testable without a UI runtime.
    /// </summary>
    public readonly record struct CombineRow(int Position, string Set);

    /// <summary>
    /// Outcome of <see cref="CombineSelectionRule.Evaluate"/>: whether the selection is a valid
    /// combine, the human-facing <see cref="Message"/> for an invalid one, and (when valid) the
    /// 0-based official indices the run covers.
    /// </summary>
    public readonly record struct CombineSelectionResult(
        bool IsValid, IReadOnlyList<int> CoveredOfficialIndices, string Message);

    /// <summary>
    /// Pure, WPF-free predicate backing the setlist editor's "Combine Selected" gesture
    /// (alias-setlists-spec.md §6.2). A selection is a valid combine iff its rows form a contiguous,
    /// ascending run of DISTINCT flattened official positions, length &gt;= 2, all within a SINGLE
    /// set — cross-set-boundary runs are rejected for v1 (spec open Q11.1 → within-set). The covered
    /// official indices are the positions mapped 1-based → 0-based (Position <c>p</c> → index
    /// <c>p - 1</c>), matching the matcher-side <c>CoveredEntryIndices</c> convention
    /// (<c>SetlistMatcher</c>). Kept out of the code-behind so the decision is unit-testable in
    /// isolation (mirrors <see cref="DeadEditor.Services.DuplicateDateRule"/> /
    /// <see cref="BoxSetPullCollision"/>).
    /// </summary>
    public static class CombineSelectionRule
    {
        /// <summary>Shown when fewer than two rows are selected.</summary>
        public const string NeedTwoMessage = "Select two or more adjacent songs to combine.";

        /// <summary>Shown when the selected rows are not a contiguous ascending run.</summary>
        public const string NotContiguousMessage =
            "Select an adjacent (contiguous) run of songs to combine.";

        /// <summary>Shown when the selected rows span more than one set.</summary>
        public const string CrossSetMessage = "A combined run must stay within a single set.";

        /// <summary>
        /// Evaluates a row selection. Order of checks: count → single-set → contiguity, so the most
        /// specific message wins (a cross-set selection reports the set rule, not contiguity).
        /// </summary>
        public static CombineSelectionResult Evaluate(IReadOnlyList<CombineRow>? rows)
        {
            if (rows == null || rows.Count < 2)
                return new CombineSelectionResult(false, Array.Empty<int>(), NeedTwoMessage);

            // Within a single set — ordinal, because set labels are exact display strings.
            var firstSet = rows[0].Set ?? "";
            if (rows.Any(r => !string.Equals(r.Set ?? "", firstSet, StringComparison.Ordinal)))
                return new CombineSelectionResult(false, Array.Empty<int>(), CrossSetMessage);

            // Contiguous ascending run of DISTINCT positions: sorted, no duplicates, no gaps.
            var positions = rows.Select(r => r.Position).OrderBy(p => p).ToList();
            bool distinct = positions.Distinct().Count() == positions.Count;
            bool contiguous = positions[^1] - positions[0] == positions.Count - 1;
            if (!distinct || !contiguous)
                return new CombineSelectionResult(false, Array.Empty<int>(), NotContiguousMessage);

            // Map 1-based editor positions → 0-based official indices.
            var covered = positions.Select(p => p - 1).ToList();
            return new CombineSelectionResult(true, covered, "");
        }

        /// <summary>
        /// True iff <paramref name="candidate"/> (0-based covered run) is order-sensitive
        /// <see cref="Enumerable.SequenceEqual{T}(IEnumerable{T}, IEnumerable{T})"/> to any run in
        /// <paramref name="existingCoveredRuns"/>. The same identity <c>TryAppendAliasEntry</c> uses,
        /// so author-time dedup (recorded ∪ pending) and Save-time append-dedup agree. Null-safe.
        /// </summary>
        public static bool IsDuplicateRun(
            IEnumerable<IReadOnlyList<int>>? existingCoveredRuns, IReadOnlyList<int> candidate)
        {
            return existingCoveredRuns != null
                && existingCoveredRuns.Any(run => run != null && run.SequenceEqual(candidate));
        }
    }
}
