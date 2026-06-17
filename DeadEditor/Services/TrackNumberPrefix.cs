using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    /// <summary>
    /// Pure helper that strips a leading track-number prefix from a track title so the
    /// remainder can be used as a canonical-lookup key. Extracted (per the
    /// <see cref="DuplicateDateRule"/> / ConcertVerifyGate precedent) so the
    /// strip rules can be unit-tested in isolation, with no UI, I/O, or service singletons.
    /// <para>
    /// Match-key-only by design: the caller feeds the result into the canonical lookup, never
    /// back into the stored tag. A false strip therefore cannot corrupt data — the stripped
    /// key simply fails the lookup and the original title is preserved. See
    /// documentation/track-number-prefix-normalization.md.
    /// </para>
    /// </summary>
    public static class TrackNumberPrefix
    {
        // Disc-track token with a leading disc/source letter: "d1t01 ", "s1t05 ".
        private static readonly Regex DiscTrackLetterRegex =
            new(@"^\s*[A-Za-z]\d+[tT]\d+\s+(?=\S)", RegexOptions.Compiled);

        // Disc-hyphen-track: "1-01 " (digits-hyphen-digits, no surrounding spaces, trailing ws).
        // Checked before the generic separator form so "1-01 Bertha" strips the whole token
        // rather than just "1-".
        private static readonly Regex DiscHyphenTrackRegex =
            new(@"^\s*\d+-\d+\s+(?=\S)", RegexOptions.Compiled);

        // Generic "digits + separator" prefix for the unambiguous separators "01 - ", "1. ",
        // "01) ", "01_", including en-dash and em-dash. Stripped regardless of whether the
        // remainder begins with a digit, so number-titled songs after a track prefix resolve
        // (e.g. "03 - 46 Days" -> "46 Days", "03 - 2001" -> "2001").
        private static readonly Regex SeparatorPrefixRegex =
            new(@"^\s*\d+\s*[-–—.)_]\s*", RegexOptions.Compiled);

        // Colon separator is ambiguous with time codes ("8:05", "12:34"), so it counts as a
        // track separator ONLY when followed by whitespace: "01: Sugaree" strips; "8:05" does not.
        private static readonly Regex ColonPrefixRegex =
            new(@"^\s*\d+\s*:\s+(?=\S)", RegexOptions.Compiled);

        // Bare-space candidate: leading digits, whitespace, then the title. Ambiguous with
        // number-titles ("16 Tons"), so only stripped when gated by a matching track number.
        private static readonly Regex BareSpaceRegex =
            new(@"^\s*(\d+)\s+(?=\S)", RegexOptions.Compiled);

        /// <summary>
        /// Removes at most one leading track-number prefix from <paramref name="title"/>,
        /// preserving the song-name remainder exactly (internal hyphens, casing, spacing).
        /// Returns <paramref name="title"/> unchanged when no prefix is present.
        /// </summary>
        /// <param name="title">Track title (raw or parser-cleaned). Null/empty returns as-is.</param>
        /// <param name="trackNumber">Optional track number used to gate the ambiguous bare-space
        /// form ("01 Bertha"): stripped only when the leading number equals <paramref name="trackNumber"/>
        /// or <c>trackNumber % 100</c> (disc-encoded numbering such as a displayed "Track 101").</param>
        public static string Strip(string title, int? trackNumber = null)
        {
            if (string.IsNullOrEmpty(title)) return title;

            // Structurally unambiguous forms — strip unconditionally.
            var m = DiscTrackLetterRegex.Match(title);
            if (m.Success) return Remainder(title, m.Length);

            m = DiscHyphenTrackRegex.Match(title);
            if (m.Success) return Remainder(title, m.Length);

            m = SeparatorPrefixRegex.Match(title);
            if (m.Success) return Remainder(title, m.Length);

            m = ColonPrefixRegex.Match(title);
            if (m.Success) return Remainder(title, m.Length);

            // Bare-space form — strip ONLY when the leading number matches the supplied
            // track number (or its disc-encoded form).
            m = BareSpaceRegex.Match(title);
            if (m.Success && trackNumber.HasValue
                && long.TryParse(m.Groups[1].Value, out var leading)
                && (leading == trackNumber.Value || leading == trackNumber.Value % 100))
            {
                return Remainder(title, m.Length);
            }

            return title;
        }

        // Never strip to nothing: an all-prefix string keeps its original value.
        private static string Remainder(string title, int prefixLength)
        {
            var rest = title.Substring(prefixLength);
            return string.IsNullOrWhiteSpace(rest) ? title : rest;
        }
    }
}
