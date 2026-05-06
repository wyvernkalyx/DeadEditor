using DeadEditor.Models;
using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Services
{
    /// <summary>
    /// Detects per-album disc/track-number inconsistencies that hand-editing in the
    /// Edit Metadata grid can introduce: duplicates within a disc, zero values, and
    /// gaps in the track sequence (with 100-format awareness).
    ///
    /// Pure function — no UI dependencies, no I/O. Issues are returned as
    /// human-readable strings prefixed with a warning glyph, ready for the
    /// validation banner above the metadata grid.
    /// </summary>
    public static class MetadataValidator
    {
        private const string WarnPrefix = "⚠ "; // ⚠

        public static IReadOnlyList<string> Validate(IEnumerable<TrackInfo> tracks)
        {
            var issues = new List<string>();
            if (tracks == null) return issues;

            var trackList = tracks.ToList();
            if (trackList.Count == 0) return issues;

            // 1. Per-track zero TrackNumber / DiscNumber
            foreach (var t in trackList.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
            {
                var label = TrackLabel(t);
                if (t.TrackNumber == 0)
                {
                    issues.Add($"{WarnPrefix}Track '{label}' has no track number");
                }
                if (t.DiscNumber == 0)
                {
                    issues.Add($"{WarnPrefix}Track '{label}' has no disc number");
                }
            }

            // 2. Per-disc duplicate + gap detection. Skip tracks with zero values
            //    (already flagged above) so they don't produce double-noise.
            var discGroups = trackList
                .Where(t => t.DiscNumber > 0 && t.TrackNumber > 0)
                .GroupBy(t => t.DiscNumber)
                .OrderBy(g => g.Key);

            foreach (var disc in discGroups)
            {
                var numbersOnDisc = disc.Select(t => t.TrackNumber).OrderBy(n => n).ToList();

                // Duplicate detection — report each colliding number once.
                var seenDups = new HashSet<int>();
                for (int i = 1; i < numbersOnDisc.Count; i++)
                {
                    if (numbersOnDisc[i] == numbersOnDisc[i - 1] && seenDups.Add(numbersOnDisc[i]))
                    {
                        issues.Add($"{WarnPrefix}Disc {disc.Key} has two tracks numbered {numbersOnDisc[i]}");
                    }
                }

                // Gap detection — first jump only per disc.
                var distinct = numbersOnDisc.Distinct().OrderBy(n => n).ToList();
                if (distinct.Count >= 2)
                {
                    int expectedStep = 1;
                    for (int i = 1; i < distinct.Count; i++)
                    {
                        if (distinct[i] != distinct[i - 1] + expectedStep)
                        {
                            issues.Add($"{WarnPrefix}Disc {disc.Key} jumps from track {distinct[i - 1]} to track {distinct[i]}");
                            break;
                        }
                    }
                }

                // Gap from base — DP43-style (102..107 with no 101). Only meaningful
                // for 100-format discs; plain format uses min as base by definition.
                int? expectedBase = ExpectedBaseForDisc(disc.Key, distinct);
                if (expectedBase.HasValue && distinct[0] > expectedBase.Value)
                {
                    issues.Add($"{WarnPrefix}Disc {disc.Key} starts at track {distinct[0]} (expected {expectedBase.Value})");
                }
            }

            return issues;
        }

        /// <summary>
        /// Heuristic format detection for a single disc:
        /// • If every number on the disc is &gt;= 100 AND each number's leading digits
        ///   match the disc number (i.e. n / 100 == disc), treat as 100-format and
        ///   expect base = disc * 100 + 1.
        /// • Otherwise treat as plain format. Plain format uses min(numbers) as the
        ///   base by definition, so no expected-start check fires (returns null).
        ///
        /// False positives lead to a warning the user can ignore; false negatives
        /// lead to a missed warning. Both are recoverable.
        /// </summary>
        private static int? ExpectedBaseForDisc(int disc, IReadOnlyList<int> numbers)
        {
            if (numbers.Count == 0) return null;
            bool isHundredFormat = numbers.All(n => n >= 100 && n / 100 == disc);
            return isHundredFormat ? disc * 100 + 1 : (int?)null;
        }

        private static string TrackLabel(TrackInfo t)
        {
            if (!string.IsNullOrWhiteSpace(t.SongName)) return t.SongName!;
            if (!string.IsNullOrWhiteSpace(t.RawTitle)) return t.RawTitle!;
            if (!string.IsNullOrWhiteSpace(t.FileName)) return t.FileName!;
            return "(unnamed)";
        }
    }
}
