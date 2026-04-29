using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    /// <summary>
    /// Result of parsing a track-title string into structured components.
    /// See <see cref="TitleStructureParser"/> and documentation/title-structure-parser-spec.md.
    /// <para>
    /// Invariants:
    ///   - <see cref="RawMetadataFragments"/> is never null; empty when nothing classified as metadata.
    ///   - <see cref="SongName"/> is never null; empty string for null/whitespace inputs.
    /// </para>
    /// </summary>
    public sealed record TitleParseResult(
        string SongName,
        bool HasSegue,
        string? TrackDate,
        string? Venue,
        IReadOnlyList<string> RawMetadataFragments);

    /// <summary>
    /// Stateless parser that decomposes a noisy track title into
    /// <c>(SongName, HasSegue, TrackDate, Venue, RawMetadataFragments)</c>.
    /// <para>
    /// This is a standalone service. As of commit Y2K-2b it is not wired into any caller —
    /// integration with <c>MetadataService.ParseTitleAndDate</c> and
    /// <c>NormalizationService.Normalize</c> is deferred to commits Y2K-2c / Y2K-2d.
    /// </para>
    /// <para>
    /// The pipeline classifies parens/brackets by content rather than stripping by position,
    /// so canonical-paren song names like <c>"Caution (Do Not Stop on Tracks)"</c> survive
    /// while metadata parens like <c>"(San Francisco, 11/2/69)"</c> are extracted.
    /// </para>
    /// </summary>
    public static class TitleStructureParser
    {
        // ----- Reference data -----

        // 50 US states + DC + 13 Canadian provinces/territories = 64 codes.
        // Inline because it is stable, no need for runtime configurability.
        private static readonly HashSet<string> StateCodes = new(StringComparer.Ordinal)
        {
            // US states
            "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA",
            "HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
            "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
            "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC",
            "SD","TN","TX","UT","VT","VA","WA","WV","WI","WY",
            "DC",
            // Canadian provinces & territories
            "AB","BC","MB","NB","NL","NS","NT","NU","ON","PE","QC","SK","YT",
        };

        // ----- Regex patterns (compiled once) -----

        // Date patterns. Tried in order ISO -> year-first slash -> US slash to disambiguate
        // strings like "1971/07/02" which would otherwise match the US-slash regex starting at "71/07/02".
        private static readonly Regex IsoDateRegex = new(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", RegexOptions.Compiled);
        private static readonly Regex YearFirstSlashRegex = new(@"\b(\d{4})/(\d{1,2})/(\d{1,2})\b", RegexOptions.Compiled);
        private static readonly Regex UsSlashRegex = new(@"\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b", RegexOptions.Compiled);

        // Suffix-strip patterns. Both are anchored to end-of-string and only fire when the
        // suffix looks distinctive enough to be metadata, not part of the song name.
        private static readonly Regex ArtistSuffixRegex = new(@"\s+-\s+[^_]+__\s*$", RegexOptions.Compiled);
        // Require whitespace on both sides of the dash so that ISO dates like "(1969-12-26)"
        // are NOT mistaken for a year-tour suffix.
        private static readonly Regex YearTourSuffixRegex = new(@"\s*\((\d{4})\s+[-–—]\s+[^)]+\)\s*$", RegexOptions.Compiled);

        // Paren / bracket scanners. Non-nested: the spec explicitly excludes nested brackets.
        // Match groups: (1) the inner content.
        private static readonly Regex ParenRegex = new(@"\(([^()]*)\)", RegexOptions.Compiled);
        private static readonly Regex BracketRegex = new(@"\[([^\[\]]*)\]", RegexOptions.Compiled);

        // Segue marker patterns. The trailing variant is anchored to end-of-string and may
        // repeat (e.g., " > > >"). The embedded variant is unanchored and only used to
        // SET HasSegue; mid-string markers are preserved per the embedded-segue rule.
        private const string SegueMarkerCore = @"(?:[-–]?>|→|\[\s*>\s*\])";
        private static readonly Regex TrailingSegueRegex = new(@"(?:\s*" + SegueMarkerCore + @")+\s*$", RegexOptions.Compiled);
        private static readonly Regex AnySegueRegex = new(SegueMarkerCore, RegexOptions.Compiled);

        // Bracket group whose entire content is a segue arrow ("[>]" or "[ > ]"). The paren
        // walker skips these so that step 5 can treat them as segue markers, not paren-bracket
        // metadata or content.
        private static readonly Regex BracketSegueOnlyRegex = new(@"^\s*>\s*$", RegexOptions.Compiled);

        // ----- Public API -----

        /// <summary>
        /// Parses a track title into structured components. Stateless and side-effect-free.
        /// </summary>
        /// <param name="title">Raw title from a tag, file name, or external source. Null/empty/whitespace
        /// inputs are handled gracefully and return an empty result.</param>
        /// <param name="albumDate">Optional album date (yyyy-MM-dd or yyyy form). Used to resolve two-digit
        /// years to the album's century where possible. See <see cref="TwoDigitYearResolver"/>.</param>
        public static TitleParseResult Parse(string? title, string? albumDate = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                return new TitleParseResult(string.Empty, HasSegue: false, TrackDate: null, Venue: null, Array.Empty<string>());

            // Step 1: cosmetic prep (apostrophes, tape markers).
            // Dash normalization is deferred to step 6 so en-dash segue markers ("–>") aren't pre-stripped.
            var working = CosmeticPrep(title);

            // Step 2: strip MB API artist-suffix tail like " - Grateful Dead__".
            working = ArtistSuffixRegex.Replace(working, string.Empty);

            // Step 3: strip year-tour suffix like " (1972 - Europe '72)" — year alone is not a track date.
            working = YearTourSuffixRegex.Replace(working, string.Empty);

            // Step 4: walk parens & brackets, classify each as metadata or content.
            var fragments = new List<string>();
            string? trackDate = null;
            string? venue = null;
            working = WalkBracketGroups(working, albumDate, fragments, ref trackDate, ref venue);

            // Step 5: handle segue markers — strip trailing only, detect embedded for HasSegue.
            working = HandleSegues(working, out bool hasSegue);

            // Step 6: normalize remaining typographic dashes to ASCII hyphen.
            working = NormalizeDashes(working);

            // Step 7: collapse whitespace and trim.
            working = Regex.Replace(working, @"\s+", " ").Trim();

            return new TitleParseResult(working, hasSegue, trackDate, venue, fragments.ToArray());
        }

        // ----- Pipeline helpers -----

        private static string CosmeticPrep(string title)
        {
            // Replace tape-flip markers; collapse smart quotes to straight ASCII apostrophe.
            var s = title.Replace("//", " ");
            s = s.Replace('’', '\'')   // RIGHT SINGLE QUOTATION MARK
                 .Replace('‘', '\'')   // LEFT SINGLE QUOTATION MARK
                 .Replace('`', '\'');
            return s;
        }

        private static string NormalizeDashes(string s)
        {
            // EN DASH (–), EM DASH (—), BOX DRAWINGS LIGHT HORIZONTAL (─) -> hyphen.
            return s.Replace('–', '-')
                    .Replace('—', '-')
                    .Replace('─', '-');
        }

        // Walks parens and brackets in left-to-right order. Each group is either:
        //   - skipped (bracket containing only ">", left in place for the segue handler)
        //   - classified as metadata (extract date+venue, append fragment, remove from working copy)
        //   - classified as content (preserved verbatim)
        // Returns the working copy with metadata groups removed.
        private static string WalkBracketGroups(
            string working,
            string? albumDate,
            List<string> fragments,
            ref string? trackDate,
            ref string? venue)
        {
            // Run paren-pass and bracket-pass independently. Order doesn't matter for the result —
            // a content paren and a content bracket don't interact, and metadata groups are removed.
            working = ProcessGroups(working, ParenRegex, isBracket: false, albumDate, fragments, ref trackDate, ref venue);
            working = ProcessGroups(working, BracketRegex, isBracket: true, albumDate, fragments, ref trackDate, ref venue);
            return working;
        }

        private static string ProcessGroups(
            string working,
            Regex regex,
            bool isBracket,
            string? albumDate,
            List<string> fragments,
            ref string? trackDate,
            ref string? venue)
        {
            // Iterate matches manually so we can replace metadata groups while leaving content groups intact.
            // We rebuild the string each iteration because indices shift after removal.
            int searchFrom = 0;
            while (true)
            {
                var m = regex.Match(working, searchFrom);
                if (!m.Success) break;

                var inner = m.Groups[1].Value;

                // Bracket-only segue marker: skip, leave in place for segue handler.
                if (isBracket && BracketSegueOnlyRegex.IsMatch(inner))
                {
                    searchFrom = m.Index + m.Length;
                    continue;
                }

                if (IsMetadata(inner))
                {
                    fragments.Add(inner);
                    var (date, dateMatched) = ExtractDate(inner, albumDate);
                    if (trackDate is null && date is not null)
                        trackDate = date;
                    var v = ExtractVenue(inner, dateMatched);
                    if (venue is null && !string.IsNullOrEmpty(v))
                        venue = v;

                    // Remove the entire group (including outer brackets) and any neighboring single space.
                    working = working.Remove(m.Index, m.Length);
                    if (m.Index > 0 && m.Index <= working.Length && working[m.Index - 1] == ' '
                        && (m.Index >= working.Length || working[m.Index] == ' ' || working[m.Index] == ')'))
                    {
                        working = working.Remove(m.Index - 1, 1);
                        searchFrom = Math.Max(0, m.Index - 1);
                    }
                    else
                    {
                        searchFrom = m.Index;
                    }
                }
                else
                {
                    // Content group: leave in place, advance past it so we don't re-evaluate.
                    searchFrom = m.Index + m.Length;
                }
            }
            return working;
        }

        // ----- Classification -----

        private static bool IsMetadata(string fragment)
        {
            if (IsoDateRegex.IsMatch(fragment)) return true;
            if (YearFirstSlashRegex.IsMatch(fragment)) return true;
            if (UsSlashRegex.IsMatch(fragment)) return true;

            if (HasStateCode(fragment)) return true;

            if (Regex.IsMatch(fragment, @"\bLive\s+(at|in)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(fragment, @"\bFiller:", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(fragment, @"\bRemaster(?:ed)?\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(fragment, @"\bReprise\b", RegexOptions.IgnoreCase)) return true;

            // Standalone "Live" inside otherwise-empty parens: "(Live)".
            if (string.Equals(fragment.Trim(), "Live", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool HasStateCode(string fragment)
        {
            // Two-letter uppercase token preceded by ", " counts (e.g., "Boston, MA").
            foreach (Match m in Regex.Matches(fragment, @"(?<=,\s*)([A-Z]{2})\b"))
            {
                if (StateCodes.Contains(m.Groups[1].Value)) return true;
            }
            // Or sole token in fragment (e.g., "(MA)").
            var trimmed = fragment.Trim();
            if (Regex.IsMatch(trimmed, @"^[A-Z]{2}$") && StateCodes.Contains(trimmed)) return true;
            return false;
        }

        // ----- Date & venue extraction -----

        private static (string? date, string? matchedSubstring) ExtractDate(string fragment, string? albumDate)
        {
            var iso = IsoDateRegex.Match(fragment);
            if (iso.Success)
            {
                var y = int.Parse(iso.Groups[1].Value);
                var mo = int.Parse(iso.Groups[2].Value);
                var d = int.Parse(iso.Groups[3].Value);
                if (IsValidDate(y, mo, d))
                    return ($"{y:D4}-{mo:D2}-{d:D2}", iso.Value);
            }

            var yfs = YearFirstSlashRegex.Match(fragment);
            if (yfs.Success)
            {
                var y = int.Parse(yfs.Groups[1].Value);
                var mo = int.Parse(yfs.Groups[2].Value);
                var d = int.Parse(yfs.Groups[3].Value);
                if (IsValidDate(y, mo, d))
                    return ($"{y:D4}-{mo:D2}-{d:D2}", yfs.Value);
            }

            var us = UsSlashRegex.Match(fragment);
            if (us.Success)
            {
                var mo = int.Parse(us.Groups[1].Value);
                var d = int.Parse(us.Groups[2].Value);
                var y = int.Parse(us.Groups[3].Value);
                if (y < 100)
                    y = TwoDigitYearResolver.ResolveTwoDigitYear(y, albumDate);
                if (IsValidDate(y, mo, d))
                    return ($"{y:D4}-{mo:D2}-{d:D2}", us.Value);
            }

            return (null, null);
        }

        private static bool IsValidDate(int year, int month, int day) =>
            month >= 1 && month <= 12 && day >= 1 && day <= 31 && year >= 1900 && year <= 2100;

        private static string? ExtractVenue(string fragment, string? matchedDateSubstring)
        {
            var v = fragment;

            // Drop the date substring (or any date-shaped text if validation rejected it).
            if (!string.IsNullOrEmpty(matchedDateSubstring))
            {
                int idx = v.IndexOf(matchedDateSubstring, StringComparison.Ordinal);
                if (idx >= 0)
                    v = v.Remove(idx, matchedDateSubstring.Length);
            }
            else
            {
                v = IsoDateRegex.Replace(v, string.Empty);
                v = YearFirstSlashRegex.Replace(v, string.Empty);
                v = UsSlashRegex.Replace(v, string.Empty);
            }

            // Strip "Live at " / "Live in " / "Filler:" prefixes.
            v = Regex.Replace(v, @"\bLive\s+(at|in)\s+", string.Empty, RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\bFiller:\s*", string.Empty, RegexOptions.IgnoreCase);

            // Drop standalone editorial tokens.
            v = Regex.Replace(v, @"\bRemastered\b", string.Empty, RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\bRemaster\b", string.Empty, RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\bReprise\b", string.Empty, RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\bLive\b", string.Empty, RegexOptions.IgnoreCase);

            // Drop orphan 4-digit year tokens (e.g., "2020" leftover from "(2020 Remaster)").
            v = Regex.Replace(v, @"\b\d{4}\b", string.Empty);

            // Collapse internal whitespace and strip outer punctuation/whitespace.
            v = Regex.Replace(v, @"\s+", " ").Trim();
            v = Regex.Replace(v, @"^[\s,\-–.]+|[\s,\-–.]+$", string.Empty);

            return string.IsNullOrEmpty(v) ? null : v;
        }

        // ----- Segue handling -----

        private static string HandleSegues(string working, out bool hasSegue)
        {
            // Trailing markers: strip and remember whether any matched.
            bool trailing = TrailingSegueRegex.IsMatch(working);
            if (trailing)
                working = TrailingSegueRegex.Replace(working, string.Empty);

            // Embedded markers (anywhere in the post-strip working copy): detect only, do not strip.
            // The mid-title two-songs-in-one-track case ("Dark Star > St. Stephen") preserves the marker
            // and second song name as content per the embedded-segue rule (see spec known-limitations).
            bool embedded = AnySegueRegex.IsMatch(working);

            hasSegue = trailing || embedded;
            return working;
        }
    }
}
