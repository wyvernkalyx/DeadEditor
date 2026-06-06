using System.Text.RegularExpressions;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free, I/O-free verify gate for a <see cref="BoxSetDefinition"/>: decides whether
    /// the box is complete enough to be marked Verified, and if not, the first failing reason.
    /// Strictly stricter than the wizard's Save validation (Save allows zero tracks and blank
    /// SongNames, and allows a blank track Date; verify requires >=1 track, every SongName, and
    /// every track Date present + well-formed). Kept out of the wizard code-behind so the rule is
    /// unit-testable in isolation (mirrors <see cref="BoxSetSaveResolution"/> /
    /// <c>EditUnverifyRule</c>). See documentation/box-set-verification-spec.md decision 3.
    /// </summary>
    public static class BoxSetVerifyGate
    {
        // yyyy-MM-dd shape — the same pattern the wizard already uses for the release date and the
        // per-track Save date validation (BoxSetWizardView.xaml.cs DateRegex). A shape check, not a
        // calendar parse, to stay consistent with that existing behavior rather than inventing a
        // stricter format here.
        private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>
        /// Evaluates the verify gate. Returns <c>(true, null)</c> when the box may be verified,
        /// else <c>(false, reason)</c> carrying the first failing condition's reason. Conditions
        /// are checked in this order: Name, ReleaseDate, track count, every SongName, every Date.
        /// </summary>
        public static (bool CanVerify, string? Reason) Evaluate(BoxSetDefinition def)
        {
            if (string.IsNullOrWhiteSpace(def.Name))
                return (false, "Name is required");

            if (!DateRegex.IsMatch(def.ReleaseDate ?? ""))
                return (false, "Release date must be yyyy-MM-dd");

            if (def.Tracks.Count == 0)
                return (false, "Add at least one track");

            // All SongNames first (per the documented order), then all Dates — so the first failing
            // reason follows the spec's condition order, not row order.
            foreach (var t in def.Tracks)
            {
                if (string.IsNullOrWhiteSpace(t.SongName))
                    return (false, $"Track {t.TrackNumber} has no song name");
            }

            foreach (var t in def.Tracks)
            {
                // Empty vs. malformed get distinct reasons (mirrors the Save validation's
                // empty-allowed / non-empty-must-parse split, but here empty is also a failure).
                if (string.IsNullOrEmpty(t.Date))
                    return (false, $"Track {t.TrackNumber} has no date");
                if (!DateRegex.IsMatch(t.Date))
                    return (false, $"Track {t.TrackNumber} date is invalid");
            }

            return (true, null);
        }
    }
}
