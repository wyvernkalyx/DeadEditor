using System;

namespace DeadEditor.Services
{
    /// <summary>
    /// Pure helpers for the unverify-on-edit and marker-dirty rules used by
    /// EditMetadataView (see documentation/verification-and-manifest-wiring-design-memo.md
    /// § 5 and § 6 Scenario A). Extracted so the rules can be unit-tested without
    /// a WPF runtime.
    /// </summary>
    public static class EditUnverifyRule
    {
        /// <summary>
        /// True iff the current value differs from the baseline. Strict
        /// string equality — no trimming, no case-folding. Null is treated
        /// as the empty string so callers can pass either.
        /// </summary>
        public static bool IsDirty(string? baseline, string? current)
        {
            return !string.Equals(baseline ?? "", current ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// True iff an edit to a field should drop the album's verified flag.
        /// Conditions:
        ///   1. The album is currently verified.
        ///   2. The field is NOT the Archivist Note (§ 2 carve-out — note
        ///      edits are curator context, not metadata changes).
        ///   3. The current value differs from the baseline.
        /// </summary>
        public static bool WouldUnverify(string? baseline, string? current, bool isVerified, bool isArchivistNote)
        {
            if (!isVerified) return false;
            if (isArchivistNote) return false;
            return IsDirty(baseline, current);
        }
    }
}
