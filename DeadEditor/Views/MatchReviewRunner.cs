using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Windows;

namespace DeadEditor
{
    /// <summary>
    /// UI-layer helper that runs the Match Setlist preview-before-apply flow:
    /// <c>ComputeProposals -> review dialog -> Apply(edited subset)</c>. It lives
    /// in the view layer (not beside the pure <see cref="SetlistMatcher"/>)
    /// because it constructs a WPF <see cref="MatchReviewWindow"/>.
    ///
    /// Synchronous by design: <see cref="Window.ShowDialog"/> blocks, so callers
    /// stay <c>sync void</c> handlers. Returns the <see cref="SetlistMatcher.MatchResult"/>
    /// of the applied (edited) set on Confirm, or <c>null</c> on Cancel — in which
    /// case nothing was written and the caller must no-op.
    /// </summary>
    public static class MatchReviewRunner
    {
        public static SetlistMatcher.MatchResult? RunReview(
            IReadOnlyList<TrackInfo> tracks,
            IReadOnlyList<SetlistMatcher.SetlistEntry> setlist,
            Func<string, string?> resolveCanonical,
            Func<string, string?> resolveOfficialOrNull,
            Window? owner = null)
        {
            var proposals = SetlistMatcher.ComputeProposals(
                tracks, setlist, resolveCanonical, resolveOfficialOrNull);
            // Derive the unmatched subset (tracks with no proposal) here — the
            // matcher emits proposals for matched tracks only, so this is the sole
            // seam that sees both the full track list and the proposals. Surfaced
            // read-only inside the review dialog (both Import and Edit flow through
            // RunReview).
            var unmatched = UnmatchedTracks.Compute(tracks, proposals);
            var vm = new MatchReviewViewModel(proposals, unmatched);
            var win = new MatchReviewWindow(vm) { Owner = owner };

            if (win.ShowDialog() != true)
            {
                // Cancel (or window dismissed): apply nothing.
                return null;
            }

            // Confirm: apply only the resolved/edited proposals. ClaimedPositions
            // rides through unchanged from compute (claim-at-compute, spec §4.1).
            return SetlistMatcher.Apply(vm.BuildEditedProposalSet());
        }
    }
}
