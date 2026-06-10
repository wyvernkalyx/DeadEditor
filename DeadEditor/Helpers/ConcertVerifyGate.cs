using System.Text.RegularExpressions;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free, I/O-free verify gate for a <see cref="ConcertReference"/>: decides whether
    /// the concert is complete enough to be marked Verified, and if not, the first failing reason.
    /// Stricter than the setlist editor's Save validation (Save only requires a well-formed date and
    /// at least one track; verify additionally requires a non-empty venue and every track to have a
    /// song name). Deliberately has NO per-track-date rule (concert tracks default their date from
    /// the concert date — a concert is single-date, unlike a box set) and NO city/state/country
    /// requirement. Kept out of the view code-behind so the rule is unit-testable in isolation
    /// (mirrors <see cref="BoxSetVerifyGate"/>). See documentation/concert-verification-spec.md
    /// decision 3.
    /// </summary>
    public static class ConcertVerifyGate
    {
        // yyyy-MM-dd shape — the same pattern BoxSetVerifyGate and the EditSetlistView save validation
        // use. A shape check, not a calendar parse, to stay consistent with that existing behavior.
        private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>
        /// Evaluates the verify gate. Returns <c>(true, null)</c> when the concert may be verified,
        /// else <c>(false, reason)</c> carrying the first failing condition's reason. Conditions are
        /// checked in this order: Date, Venue, track count, every track's SongName. A null concert or
        /// null track list fails gracefully with a reason rather than throwing.
        /// </summary>
        public static (bool CanVerify, string? Reason) Evaluate(ConcertReference concert)
        {
            if (concert == null)
                return (false, "Concert data is missing");

            if (!DateRegex.IsMatch(concert.Date ?? ""))
                return (false, "Date must be yyyy-MM-dd");

            if (string.IsNullOrWhiteSpace(concert.Venue))
                return (false, "Venue is required");

            if (concert.Tracks == null || concert.Tracks.Count == 0)
                return (false, "Add at least one track");

            foreach (var t in concert.Tracks)
            {
                if (string.IsNullOrWhiteSpace(t.SongName))
                    return (false, $"Track {t.Position} has no song name");
            }

            return (true, null);
        }
    }
}
