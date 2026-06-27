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

        public MatchReviewViewModel(SetlistMatcher.ProposalSet proposalSet)
        {
            _source = proposalSet;
            AllRows = proposalSet.Proposals
                .Select(p => new ReviewRowViewModel(p))
                .ToList();
            VisibleRows = AllRows.Where(r => !r.IsNoOp).ToList();
            HiddenCount = AllRows.Count(r => r.IsNoOp);
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

        /// <summary>The collapsed-count line for the hidden no-op rows. Exact
        /// wording is a deferred UX detail (spec §10).</summary>
        public string HiddenSummary => $"{HiddenCount} tracks already match";

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
