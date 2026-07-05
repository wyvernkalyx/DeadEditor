using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.Linq;

namespace DeadEditor
{
    /// <summary>
    /// Container view-model for the Match Setlist review surface, built from a
    /// <see cref="SetlistMatcher.ProposalSet"/> produced by
    /// <see cref="SetlistMatcher.ComputeProposals"/>. Holds one
    /// <see cref="ReviewRowViewModel"/> per proposal (including no-ops), exposes
    /// the visible (non-no-op) subset plus the collapsed no-op count, and
    /// rebuilds the resolved <see cref="SetlistMatcher.ProposalSet"/> the host
    /// hands to <see cref="SetlistMatcher.Apply"/>.
    ///
    /// The rebuild follows the "edited-New, never drop rows" decision: every
    /// proposal — hidden no-ops included — is carried through with its New
    /// values rewritten to the row's effective values, so Apply still sets
    /// IsMatched on all claimed tracks (all were claimed at compute) and flags
    /// IsModified only where Effective actually differs from Old.
    /// <see cref="SetlistMatcher.ProposalSet.ClaimedPositions"/> is carried
    /// through unchanged (claim-at-compute, spec §4.1).
    /// </summary>
    public class MatchReviewViewModel
    {
        private readonly SetlistMatcher.ProposalSet _source;

        /// <param name="unmatchedTracks">The tracks the compute produced no
        /// proposal for, derived by the host from the full track list minus the
        /// proposals' tracks (<see cref="Helpers.UnmatchedTracks"/>). Surfaced
        /// read-only so the "N unmatched" flag lands inside the dialog rather than
        /// only in the grid highlight/status line behind it. Optional (defaults to
        /// empty) so callers/tests that only exercise the proposal rows are
        /// unaffected.</param>
        public MatchReviewViewModel(
            SetlistMatcher.ProposalSet proposalSet,
            IReadOnlyList<TrackInfo>? unmatchedTracks = null)
        {
            _source = proposalSet;
            AllRows = proposalSet.Proposals
                .Select(p => new ReviewRowViewModel(p))
                .ToList();
            // A combined row is always shown for confirmation, exempt from no-op
            // hiding (spec §6.1). A row is hidden iff it is a no-op AND not a combine.
            VisibleRows = AllRows.Where(r => !r.IsNoOp || r.IsCombined).ToList();
            HiddenCount = AllRows.Count(r => r.IsNoOp && !r.IsCombined);

            UnmatchedRows = (unmatchedTracks ?? new List<TrackInfo>())
                .Select(t => new UnmatchedRowViewModel(t))
                .ToList();
        }

        /// <summary>Every row, including no-ops (the rebuild needs them all).</summary>
        public IReadOnlyList<ReviewRowViewModel> AllRows { get; }

        /// <summary>The rows the dialog renders: those with a real change
        /// (conjunction no-op hiding, spec §4).</summary>
        public IReadOnlyList<ReviewRowViewModel> VisibleRows { get; }

        /// <summary>How many rows are hidden no-ops (already match).</summary>
        public int HiddenCount { get; }

        /// <summary>Whether the collapsed no-op line should show at all.</summary>
        public bool HasHidden => HiddenCount > 0;

        /// <summary>The collapsed-count line for the hidden no-op rows, with
        /// grammatical singular/plural. Exact wording is a deferred UX detail
        /// (spec §10).</summary>
        public string HiddenSummary => HiddenCount == 1
            ? "1 track already matches"
            : $"{HiddenCount} tracks already match";

        /// <summary>The read-only Unmatched section rows: one per track the
        /// compute produced no proposal for. Pure display — no accept/reject
        /// state — because an unmatched track has nothing to accept; it is
        /// resolved manually in the grid behind the dialog.</summary>
        public IReadOnlyList<UnmatchedRowViewModel> UnmatchedRows { get; }

        /// <summary>How many tracks went unmatched.</summary>
        public int UnmatchedCount => UnmatchedRows.Count;

        /// <summary>Whether the Unmatched section should show at all (mirrors
        /// <see cref="HasHidden"/>).</summary>
        public bool HasUnmatched => UnmatchedCount > 0;

        /// <summary>The Unmatched section header line, with grammatical
        /// singular/plural (mirrors <see cref="HiddenSummary"/>). Points the user
        /// at the Setlist panel, the always-available assign path (reference-side-panel-spec.md
        /// §7.5, §7.6c) — not the sometimes-absent right-click menu.</summary>
        public string UnmatchedSummary => UnmatchedCount == 1
            ? "1 unmatched track — assign from the Setlist panel"
            : $"{UnmatchedCount} unmatched tracks — assign from the Setlist panel";

        /// <summary>
        /// Builds a fresh <see cref="SetlistMatcher.ProposalSet"/> from the
        /// resolved rows. One new proposal per row (all rows, hidden included),
        /// Track and CoveredEntryIndices preserved, Old values preserved, and
        /// New values set to the row's effective (accept/ignore/edited) values.
        /// ClaimedPositions is carried through unchanged. The source proposals
        /// are never mutated.
        /// </summary>
        public SetlistMatcher.ProposalSet BuildEditedProposalSet()
        {
            var proposals = AllRows
                .Select(r => new SetlistMatcher.TrackProposal
                {
                    Track = r.Source.Track,
                    OldSongName = r.Source.OldSongName,
                    NewSongName = r.EffectiveSongName,
                    OldSegue = r.Source.OldSegue,
                    NewSegue = r.EffectiveSegue,
                    CoveredEntryIndices = r.Source.CoveredEntryIndices,
                })
                .ToList();

            return new SetlistMatcher.ProposalSet
            {
                Proposals = proposals,
                ClaimedPositions = _source.ClaimedPositions,
            };
        }
    }
}
