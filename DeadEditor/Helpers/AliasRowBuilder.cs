using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// One display row in the setlist editor's combine list (alias-setlists-spec.md §6.2, removal).
    /// Carries the covered official indices, a human label, and the two origin/state flags the ✕/↺
    /// row control branches on: <see cref="IsPendingAppend"/> (a combine staged this session, not yet
    /// on disk — its ✕ drops it from the pending-append buffer) and <see cref="IsStagedForRemoval"/>
    /// (a recorded combine the user has staged for removal — shown struck-through with a ↺ to
    /// un-stage). A recorded row that is not staged has both flags false. A WPF-free record so the
    /// builder below stays unit-testable without a UI runtime (mirrors <see cref="CombineSelectionRule"/>).
    /// </summary>
    public sealed record AliasRow(
        IReadOnlyList<int> CoveredIndices, string Label, bool IsPendingAppend, bool IsStagedForRemoval);

    /// <summary>
    /// Pure, WPF-free builder for the editor's combine display rows. Composes the net row set from the
    /// three editor buffers — recorded entries (<c>_concert.AliasSetlists</c>), pending removals
    /// (<c>_pendingAliasRemovals</c>), and pending appends (<c>_pendingAliasEntries</c>) — so the glue
    /// in <c>EditSetlistView</c> is a thin projection + bind. Recorded rows come first (a recorded run
    /// present in the removals buffer is KEPT in the list, flagged <see cref="AliasRow.IsStagedForRemoval"/>
    /// so the UI can strike it through rather than hide it), then pending-append rows. Identity is the
    /// order-sensitive <see cref="Enumerable.SequenceEqual{T}(IEnumerable{T}, IEnumerable{T})"/> the
    /// append/remove helpers use (covered runs are contiguous ascending, so order is identity). The
    /// label is produced by an injected <paramref name="labeler"/> so the builder never touches the
    /// editor's live <c>_tracks</c>. Null inputs are treated as empty. Kept out of the code-behind so
    /// the row composition is unit-testable in isolation.
    /// </summary>
    public static class AliasRowBuilder
    {
        public static List<AliasRow> Build(
            IEnumerable<IReadOnlyList<int>>? recorded,
            IEnumerable<IReadOnlyList<int>>? pendingRemovals,
            IEnumerable<IReadOnlyList<int>>? pendingAppends,
            Func<IReadOnlyList<int>, string> labeler)
        {
            var removals = pendingRemovals?.ToList() ?? new List<IReadOnlyList<int>>();
            var rows = new List<AliasRow>();

            foreach (var covered in recorded ?? Enumerable.Empty<IReadOnlyList<int>>())
            {
                bool staged = removals.Any(r => r != null && r.SequenceEqual(covered));
                rows.Add(new AliasRow(covered, labeler(covered),
                    IsPendingAppend: false, IsStagedForRemoval: staged));
            }

            foreach (var covered in pendingAppends ?? Enumerable.Empty<IReadOnlyList<int>>())
            {
                rows.Add(new AliasRow(covered, labeler(covered),
                    IsPendingAppend: true, IsStagedForRemoval: false));
            }

            return rows;
        }
    }
}
