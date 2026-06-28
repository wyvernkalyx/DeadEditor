using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    /// <summary>
    /// Pure, I/O-free decomposer for combined media track names (e.g. the official
    /// "Help On The Way/Slipknot!" pressing, or a taper's "Space > The Other One"). Given a
    /// combined name, the ordered canonical names of a flattened setlist, and a canonical resolver,
    /// it returns the contiguous run of official entry indices the combined name covers, or null
    /// when the name is atomic / invalid / not a contiguous in-order run.
    ///
    /// No NormalizationService dependency — the resolver is injected (same <see cref="Func{T,TResult}"/>
    /// shape the matcher takes, <c>GetOfficialTitle</c>: official title or null), so slice 3 can pass
    /// the identical resolver and tests need no songs.json. See alias-setlists-spec.md sections 5.1
    /// (atomicity guard) and 5.2 (validation gate).
    /// </summary>
    public static class CombinedTrackDecomposer
    {
        // Candidate separators after the atomicity guard (spec 5.1). "->" alternates before ">" so
        // an arrow is consumed as a unit; optional surrounding whitespace is absorbed.
        private static readonly Regex SeparatorPattern =
            new(@"\s*(?:->|>|/)\s*", RegexOptions.Compiled);

        /// <summary>
        /// Returns the contiguous official entry indices <paramref name="mediaName"/> covers, in
        /// order, or null if the name is atomic (resolves whole), splits into fewer than two
        /// components, has a component that does not normalize to a real song, or whose components
        /// are not a contiguous in-order run of <paramref name="officialCanonicalNames"/>.
        /// First contiguous run only. Claim-blind — equivalent to the claim-aware overload with an
        /// empty claimed set; pure tests use this form.
        /// </summary>
        /// <param name="mediaName">The combined media track name as it appears on the recording.</param>
        /// <param name="officialCanonicalNames">The setlist entries' Canonical names, in order.</param>
        /// <param name="resolveOfficial">Maps a name to its official title, or null if unknown.</param>
        public static IReadOnlyList<int>? Decompose(
            string mediaName,
            IReadOnlyList<string> officialCanonicalNames,
            Func<string, string?> resolveOfficial)
            => Decompose(mediaName, officialCanonicalNames, resolveOfficial, EmptyClaimed);

        /// <summary>
        /// Claim-aware overload (spec 5.3): returns the first contiguous run that equals the
        /// components AND contains no position in <paramref name="claimedPositions"/>, or null. The
        /// matcher's pass 2 passes the positions already claimed by direct matches, so a combine can
        /// never claim a position a direct match took. All other rules (atomicity guard, split,
        /// component normalization, contiguity) are identical to the claim-blind form.
        /// </summary>
        /// <param name="claimedPositions">Official positions already claimed; windows overlapping any
        /// are skipped.</param>
        public static IReadOnlyList<int>? Decompose(
            string mediaName,
            IReadOnlyList<string> officialCanonicalNames,
            Func<string, string?> resolveOfficial,
            ISet<int> claimedPositions)
        {
            if (resolveOfficial == null) throw new ArgumentNullException(nameof(resolveOfficial));
            if (officialCanonicalNames == null) throw new ArgumentNullException(nameof(officialCanonicalNames));
            if (claimedPositions == null) throw new ArgumentNullException(nameof(claimedPositions));
            if (string.IsNullOrEmpty(mediaName)) return null;

            // 1. Atomicity guard (5.1): a whole name that resolves to a known title/alias is atomic
            //    and must never be split — this is what protects the "/"-medley official title and
            //    the ">"-bearing "I Know You Rider> High Time tease" alias.
            if (!string.IsNullOrEmpty(resolveOfficial(mediaName)))
                return null;

            // 2. Split on the separator set; trim each component.
            var rawParts = SeparatorPattern.Split(mediaName);
            if (rawParts.Length < 2) return null;

            var componentOfficials = new List<string>(rawParts.Length);
            foreach (var raw in rawParts)
            {
                var component = raw.Trim();
                // Any empty component (e.g. a trailing/leading separator) disqualifies the split.
                if (component.Length == 0) return null;

                // 3. Normalize components (5.2): an unknown component is never fabricated into a match.
                var official = resolveOfficial(component);
                if (string.IsNullOrEmpty(official)) return null;

                componentOfficials.Add(official);
            }

            // A single normalized component is the direct gate's job, not a combine.
            if (componentOfficials.Count < 2) return null;

            // 4. Contiguity (5.2): find the first contiguous in-order window equal to the components
            //    with no claimed position. No match (non-adjacent, reordered, or fully claimed) -> null.
            return FindContiguousRun(componentOfficials, officialCanonicalNames, claimedPositions);
        }

        private static readonly ISet<int> EmptyClaimed = new HashSet<int>();

        // First contiguous window [i .. i+k-1] of officialCanonicalNames whose entries equal
        // components in order (OrdinalIgnoreCase, matching the matcher) AND contains no claimed
        // position. null when none.
        private static IReadOnlyList<int>? FindContiguousRun(
            List<string> components, IReadOnlyList<string> officialCanonicalNames, ISet<int> claimedPositions)
        {
            int k = components.Count;
            int last = officialCanonicalNames.Count - k;
            for (int i = 0; i <= last; i++)
            {
                bool allMatch = true;
                for (int j = 0; j < k; j++)
                {
                    // Skip the whole window if any position in it is already claimed.
                    if (claimedPositions.Contains(i + j) ||
                        !string.Equals(components[j], officialCanonicalNames[i + j],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    var indices = new List<int>(k);
                    for (int j = 0; j < k; j++)
                        indices.Add(i + j);
                    return indices;
                }
            }

            return null;
        }
    }
}
