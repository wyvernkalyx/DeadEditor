using System;
using System.Collections.Generic;
using DeadEditor.Models;
using DeadEditor.Services;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure flatten/canonicalize/position projection of a setlist (<c>List&lt;SetInfo&gt;</c>) into
    /// the ordered <see cref="SetlistEntryVm"/> shape shared by the Match Setlist flatten and the
    /// reference side-panel (reference-side-panel-spec.md §5.1).
    ///
    /// WPF-free and side-effect-free: the canonical resolver is supplied by the caller
    /// (ImportView / EditMetadataView pass <c>NormalizationService.GetOfficialTitle</c>), so the
    /// helper never touches a service singleton and is unit-testable directly.
    ///
    /// Mirrors the flatten historically inlined in <c>MatchSetlistButton_Click</c>: a single global
    /// 0-based <see cref="SetlistEntryVm.Position"/> across all sets; <see cref="SetlistEntryVm.Canonical"/>
    /// = <c>resolver(name) ?? name</c> (echo on a miss); <see cref="SetlistEntryVm.Segue"/> carried
    /// verbatim; and a per-entry <see cref="SetlistEntryVm.SetLabel"/> (<c>"{set.Label}, #{trackWithinSet}"</c>)
    /// folded in here instead of being re-derived per position via <c>ShowLookupService.GetDiscTrack</c>.
    /// </summary>
    public static class SetlistProjection
    {
        /// <summary>
        /// Flattens <paramref name="setlist"/> into an ordered projection. Returns an empty list for a
        /// null setlist (matching the "no setlist" contract the callers already handle upstream).
        /// </summary>
        public static IReadOnlyList<SetlistEntryVm> Build(
            IReadOnlyList<SetInfo>? setlist,
            Func<string, string?> canonicalResolver)
        {
            var result = new List<SetlistEntryVm>();
            if (setlist == null)
                return result;

            int position = 0;
            foreach (var set in setlist)
            {
                var songs = set.Songs;
                if (songs == null)
                    continue;

                for (int i = 0; i < songs.Count; i++)
                {
                    var song = songs[i];
                    var canonical = canonicalResolver(song.Name) ?? song.Name;
                    var setLabel = $"{set.Label}, #{i + 1}";
                    result.Add(new SetlistEntryVm(song.Name, canonical, position, song.Segue, setLabel));
                    position++;
                }
            }

            return result;
        }
    }
}
