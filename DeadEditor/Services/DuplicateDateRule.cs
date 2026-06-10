using System;

namespace DeadEditor.Services
{
    /// <summary>
    /// Pure helper for the duplicate-date refusal in the setlist editor's save path
    /// (see documentation/add-concert-spec.md Decision 1). Extracted so the rule can be
    /// unit-tested without a WPF runtime or the ConcertLookupService singleton — the caller
    /// supplies the existence fact; the rule owns only the exclusion logic.
    /// </summary>
    public static class DuplicateDateRule
    {
        /// <summary>
        /// True iff saving under <paramref name="date"/> would clobber a DIFFERENT existing
        /// record. Conditions:
        ///   1. <paramref name="dateExistsInStore"/> — a record is already keyed under this date.
        ///   2. <paramref name="date"/> differs from <paramref name="originalDate"/> (Ordinal).
        ///
        /// Condition 2 is the load-bearing exclusion: a no-change re-save of an existing concert
        /// (date unchanged) is the record saving ITSELF, not a collision, and must pass. A New
        /// Concert carries <paramref name="originalDate"/> == "" (it has no prior file), so any
        /// already-taken date collides — exactly the intent. The comparison is Ordinal because the
        /// date is the filename + cache key (concerts/{date}.json): identity is the exact string,
        /// not a culture-aware notion of equality.
        /// </summary>
        public static bool IsCollision(bool dateExistsInStore, string date, string originalDate)
        {
            return dateExistsInStore && !string.Equals(date, originalDate, StringComparison.Ordinal);
        }
    }
}
