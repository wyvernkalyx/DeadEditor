using System;

namespace DeadEditor.Services
{
    /// <summary>
    /// Resolves two-digit years (00-99) found in track titles to four-digit years.
    /// See documentation/12-normalization-service.md § Two-Digit Year Resolution for full
    /// rationale, including the Y2K fix that replaced a hard-coded "70" pivot at four call
    /// sites in MetadataService and NormalizationService.
    /// </summary>
    public static class TwoDigitYearResolver
    {
        /// <summary>
        /// Resolves a two-digit year to a four-digit year using two rules:
        /// 1. If <paramref name="albumDate"/> provides a parseable year, prefer the album's
        ///    century when the resolved four-digit year is within ±1 of the album year.
        /// 2. Otherwise pivot at <paramref name="currentYear"/>+5: years ≤ pivot map to the
        ///    current century, years &gt; pivot map to the previous century.
        /// </summary>
        /// <param name="twoDigitYear">Year in [0,99]. Out-of-range values are returned unchanged.</param>
        /// <param name="albumDate">Optional album date in yyyy-MM-dd or yyyy form (anything where the leading 4 chars parse as int).</param>
        /// <param name="currentYear">Override for DateTime.Now.Year. Test seam.</param>
        /// <returns>Four-digit year, or <paramref name="twoDigitYear"/> unchanged if it was outside [0,99].</returns>
        public static int ResolveTwoDigitYear(int twoDigitYear, string? albumDate = null, int? currentYear = null)
        {
            if (twoDigitYear < 0 || twoDigitYear > 99)
                return twoDigitYear;

            // Rule 1: Album-date-aware resolution.
            // ±1 fuzz handles year-boundary cases (album dated 1971-12-31, track from 1972-01).
            if (TryParseLeadingYear(albumDate, out var albumYear))
            {
                var albumCentury = albumYear / 100;
                var resolved = albumCentury * 100 + twoDigitYear;
                if (Math.Abs(resolved - albumYear) <= 1)
                    return resolved;
            }

            // Rule 2: Pivot at currentYear+5. Robust across century boundaries.
            var now = currentYear ?? DateTime.Now.Year;
            var pivot = now + 5;
            var century = pivot / 100;
            var currentCenturyCandidate = century * 100 + twoDigitYear;
            return currentCenturyCandidate <= pivot
                ? currentCenturyCandidate
                : (century - 1) * 100 + twoDigitYear;
        }

        private static bool TryParseLeadingYear(string? albumDate, out int year)
        {
            year = 0;
            if (string.IsNullOrWhiteSpace(albumDate) || albumDate.Length < 4)
                return false;
            return int.TryParse(albumDate.AsSpan(0, 4), out year) && year > 0;
        }
    }
}
