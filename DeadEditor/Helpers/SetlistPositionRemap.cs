using System;
using System.Collections.Generic;
using System.Linq;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, atomic old→new position mapping for a single position-shifting setlist edit
    /// (setlist-extras-writeback-spec.md §3.3 / D9a, slice 4). A canonical setlist edit that
    /// inserts, removes, or reorders a flattened entry shifts the <b>single flattened
    /// <c>Position</c> axis</b> (0-based) that every position-indexed consumer keys on:
    /// <list type="bullet">
    ///   <item><see cref="AliasEntry.CoveredOfficialIndices"/> — combine coverage over flattened
    ///   official positions (alias-setlists-spec.md §3.2; row position <c>p</c> → index <c>p-1</c>,
    ///   so covered indices are 0-based on the same axis as claims).</item>
    ///   <item>The matcher's <c>ClaimedPositions</c> (<see cref="HashSet{Int32}"/>) and each track VM's
    ///   transient <c>ClaimedSetlistPosition</c> (<see cref="Nullable{Int32}"/>) — the §2.2 D9a
    ///   highest-risk obligation: a stamped back-reference left pointing at a shifted index silently
    ///   un-dims or mis-frees the wrong entry.</item>
    /// </list>
    ///
    /// <para><b>This component owns the remap</b> (spec §10 slice 4). It only <i>maps data</i>: it does
    /// not touch view state, matcher logic, <c>ConcertLookupService</c> write paths, or
    /// <c>CombinedTrackDecomposer</c>'s matching. Consumers (slice 5 save projection; the banked
    /// Edit-return refinement) wire the per-consumer <c>Map*</c> calls into their own state.</para>
    ///
    /// <para><b>Atomicity (all-or-nothing).</b> The component is pure: it <i>never mutates its inputs</i>
    /// — every <c>Map*</c> / <see cref="ApplyTo"/> returns fresh objects, so a caller adopts a result only
    /// after it is produced without exception. Invalid edits are rejected <i>before</i> any output is
    /// built: the factories validate their arguments (a bad insert/remove index or a non-permutation
    /// throws before a map exists), and <see cref="MapCoveredIndices"/> / <see cref="MapAliasSetlists"/>
    /// / <see cref="ApplyTo"/> throw <see cref="SetlistRemapUnsupportedException"/> the moment they meet
    /// an alias covered index the edit <i>removed</i> (the §3.3 STOP-point, semantics unspecified) — the
    /// throw precedes any adopted mutation, so on failure every input is left exactly as it was.</para>
    /// </summary>
    public sealed class SetlistPositionRemap
    {
        // _forward[oldPosition] = new position, or null if the edit removed that old position.
        private readonly int?[] _forward;

        /// <summary>Entry count on the pre-edit axis (the valid old-position domain is 0..OldCount-1).</summary>
        public int OldCount { get; }

        /// <summary>Entry count on the post-edit axis.</summary>
        public int NewCount { get; }

        private SetlistPositionRemap(int?[] forward, int oldCount, int newCount)
        {
            _forward = forward;
            OldCount = oldCount;
            NewCount = newCount;
        }

        // ===== Factories for the ratified edit kinds (§3.3: insert / removal / reorder) =====

        /// <summary>
        /// No-op edit: identity over <paramref name="count"/> positions. A remap that changes nothing,
        /// so applying it is byte-identical to the input.
        /// </summary>
        public static SetlistPositionRemap Identity(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var forward = new int?[count];
            for (int i = 0; i < count; i++) forward[i] = i;
            return new SetlistPositionRemap(forward, count, count);
        }

        /// <summary>
        /// Insert one new slot at <paramref name="insertAt"/> (0-based, valid 0..<paramref name="oldCount"/>
        /// inclusive — <paramref name="oldCount"/> appends at the tail). Old positions at or after the
        /// insertion point shift +1; positions before it are unchanged. The inserted slot takes no old
        /// position (it is new).
        /// </summary>
        public static SetlistPositionRemap ForInsert(int oldCount, int insertAt)
        {
            if (oldCount < 0) throw new ArgumentOutOfRangeException(nameof(oldCount));
            if (insertAt < 0 || insertAt > oldCount)
                throw new ArgumentOutOfRangeException(nameof(insertAt));

            var forward = new int?[oldCount];
            for (int i = 0; i < oldCount; i++)
                forward[i] = i < insertAt ? i : i + 1;
            return new SetlistPositionRemap(forward, oldCount, oldCount + 1);
        }

        /// <summary>
        /// Remove the slot at <paramref name="removeAt"/> (0-based, valid 0..<paramref name="oldCount"/>-1).
        /// The removed position maps to <c>null</c>; positions after it shift -1; positions before it are
        /// unchanged.
        /// </summary>
        public static SetlistPositionRemap ForRemove(int oldCount, int removeAt)
        {
            if (oldCount < 1) throw new ArgumentOutOfRangeException(nameof(oldCount));
            if (removeAt < 0 || removeAt >= oldCount)
                throw new ArgumentOutOfRangeException(nameof(removeAt));

            var forward = new int?[oldCount];
            for (int i = 0; i < oldCount; i++)
                forward[i] = i == removeAt ? (int?)null : (i < removeAt ? i : i - 1);
            return new SetlistPositionRemap(forward, oldCount, oldCount - 1);
        }

        /// <summary>
        /// General reorder: <paramref name="newOrder"/>[newPosition] = oldPosition, a permutation of
        /// 0..N-1. Insert/remove are the common cases with their own factories; this covers an arbitrary
        /// row re-sequence (e.g. the editor's save projection reassigning contiguous positions after a
        /// drag). Throws <see cref="ArgumentException"/> if <paramref name="newOrder"/> is not a
        /// permutation (missing/duplicate/out-of-range index) — validated before any map is built.
        /// </summary>
        public static SetlistPositionRemap ForReorder(IReadOnlyList<int> newOrder)
        {
            if (newOrder == null) throw new ArgumentNullException(nameof(newOrder));
            int n = newOrder.Count;

            var forward = new int?[n];
            var seen = new bool[n];
            for (int newPos = 0; newPos < n; newPos++)
            {
                int oldPos = newOrder[newPos];
                if (oldPos < 0 || oldPos >= n || seen[oldPos])
                    throw new ArgumentException(
                        "newOrder must be a permutation of 0..N-1 (missing, duplicate, or out-of-range index).",
                        nameof(newOrder));
                seen[oldPos] = true;
                forward[oldPos] = newPos;
            }
            return new SetlistPositionRemap(forward, n, n);
        }

        // ===== Apply — pure; inputs are never mutated =====

        /// <summary>
        /// Maps a single old position to its new position, or <c>null</c> if the edit removed it. Strict:
        /// throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="oldPosition"/> is outside
        /// 0..<see cref="OldCount"/>-1 (a bug or stale index should surface here; the tolerant claim
        /// helpers below drop out-of-range instead).
        /// </summary>
        public int? MapPosition(int oldPosition)
        {
            if (oldPosition < 0 || oldPosition >= OldCount)
                throw new ArgumentOutOfRangeException(nameof(oldPosition));
            return _forward[oldPosition];
        }

        /// <summary>
        /// Maps one track's transient <c>ClaimedSetlistPosition</c>. <c>null</c> stays <c>null</c>
        /// (nothing claimed); a claim on a removed position becomes <c>null</c> (its entry no longer
        /// exists); an out-of-range claim becomes <c>null</c> (tolerant — a stale back-reference is
        /// treated as no-longer-valid rather than throwing). Otherwise returns the shifted position.
        /// </summary>
        public int? MapClaim(int? claimedPosition)
        {
            if (claimedPosition is not int p) return null;
            if (p < 0 || p >= OldCount) return null;
            return _forward[p];
        }

        /// <summary>
        /// Remaps a matcher <c>ClaimedPositions</c> set. Removed positions are dropped; out-of-range
        /// positions are dropped (tolerant); the result is a NEW <see cref="HashSet{Int32}"/> (input
        /// untouched, duplicates naturally collapsed).
        /// </summary>
        public HashSet<int> MapClaims(IEnumerable<int> claimedPositions)
        {
            if (claimedPositions == null) throw new ArgumentNullException(nameof(claimedPositions));
            var result = new HashSet<int>();
            foreach (var p in claimedPositions)
            {
                if (p < 0 || p >= OldCount) continue;   // tolerant: drop stale/out-of-range
                if (_forward[p] is int np) result.Add(np); // removed positions (null) drop out
            }
            return result;
        }

        /// <summary>
        /// Remaps one alias entry's covered official indices, returning a NEW ascending list (input
        /// untouched). Throws <see cref="SetlistRemapUnsupportedException"/> if the edit REMOVED any
        /// covered index — the removed-covered-entry case is unspecified (§3.3 STOP-point) and must not
        /// be invented (silently shrinking a run or dropping the alias would corrupt a stored combine).
        /// A covered index outside 0..<see cref="OldCount"/>-1 is corrupt alias data and throws
        /// <see cref="ArgumentOutOfRangeException"/> (strict — never silently dropped, which would change
        /// the alias's meaning).
        /// </summary>
        public List<int> MapCoveredIndices(IReadOnlyList<int> coveredIndices)
        {
            if (coveredIndices == null) throw new ArgumentNullException(nameof(coveredIndices));

            var mapped = new List<int>(coveredIndices.Count);
            foreach (var idx in coveredIndices)
            {
                if (idx < 0 || idx >= OldCount)
                    throw new ArgumentOutOfRangeException(nameof(coveredIndices),
                        $"Covered index {idx} is outside the setlist (0..{OldCount - 1}) — corrupt alias data.");
                if (_forward[idx] is int np)
                    mapped.Add(np);
                else
                    throw new SetlistRemapUnsupportedException(idx);
            }
            mapped.Sort();
            return mapped;
        }

        /// <summary>
        /// Remaps every alias entry across a concert's <c>AliasSetlists</c>, returning NEW
        /// <see cref="AliasSetlist"/>/<see cref="AliasEntry"/> objects (inputs untouched; Id/Label copied).
        /// Atomic: throws <see cref="SetlistRemapUnsupportedException"/> on the first removed-covered index
        /// before returning anything, so on failure the caller's alias data is unchanged.
        /// </summary>
        public List<AliasSetlist> MapAliasSetlists(IReadOnlyList<AliasSetlist> aliasSetlists)
        {
            if (aliasSetlists == null) throw new ArgumentNullException(nameof(aliasSetlists));

            var result = new List<AliasSetlist>(aliasSetlists.Count);
            foreach (var alias in aliasSetlists)
            {
                var remappedEntries = alias.Entries
                    .Select(e => new AliasEntry { CoveredOfficialIndices = MapCoveredIndices(e.CoveredOfficialIndices) })
                    .ToList();
                result.Add(new AliasSetlist { Id = alias.Id, Label = alias.Label, Entries = remappedEntries });
            }
            return result;
        }

        /// <summary>
        /// Atomically remaps both data-level consumers together — alias coverage and a claimed-positions
        /// set — returning fresh objects. The alias remap runs first, so a removed-covered index throws
        /// <see cref="SetlistRemapUnsupportedException"/> before the claim set is produced and before the
        /// caller adopts anything: all-or-nothing across both consumers. Per-track
        /// <c>ClaimedSetlistPosition</c> is remapped by consumers via <see cref="MapClaim"/> (the
        /// component does not touch view state).
        /// </summary>
        public RemapResult ApplyTo(IReadOnlyList<AliasSetlist> aliasSetlists, IEnumerable<int> claimedPositions)
        {
            var aliases = MapAliasSetlists(aliasSetlists); // throws first on the STOP-point → nothing adopted
            var claims = MapClaims(claimedPositions);
            return new RemapResult(aliases, claims);
        }
    }

    /// <summary>Bundled output of <see cref="SetlistPositionRemap.ApplyTo"/> — remapped alias data + claim set.</summary>
    public sealed class RemapResult
    {
        public List<AliasSetlist> AliasSetlists { get; }
        public HashSet<int> ClaimedPositions { get; }

        public RemapResult(List<AliasSetlist> aliasSetlists, HashSet<int> claimedPositions)
        {
            AliasSetlists = aliasSetlists;
            ClaimedPositions = claimedPositions;
        }
    }

    /// <summary>
    /// Thrown when a position-shifting edit removes an entry that an <see cref="AliasEntry"/> covers.
    /// The remap of that alias is <b>unspecified</b> (setlist-extras-writeback-spec.md §3.3, slice-4
    /// STOP-point): shrinking the covered run, dropping the entry, or invalidating the whole alias are
    /// all plausible and none is ratified. The component refuses rather than invent semantics; this
    /// keeps the operation atomic (nothing mutated) until the owner rules.
    /// </summary>
    public sealed class SetlistRemapUnsupportedException : InvalidOperationException
    {
        /// <summary>The old covered index the edit removed.</summary>
        public int RemovedCoveredIndex { get; }

        public SetlistRemapUnsupportedException(int removedCoveredIndex)
            : base($"Cannot remap alias coverage: the edit removed covered index {removedCoveredIndex}, " +
                   "and removed-covered-entry semantics are unspecified (setlist-extras-writeback-spec.md §3.3, slice-4 STOP-point).")
        {
            RemovedCoveredIndex = removedCoveredIndex;
        }
    }
}
