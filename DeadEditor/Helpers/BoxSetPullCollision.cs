using System;
using System.Collections.Generic;
using System.Linq;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// The curator's choice when "pull setlist for date" targets a date that already has
    /// tracks in the box (pull-collision handling). Mirrors the enum-return shape of the
    /// import path's <c>ConflictAction</c>, but with verbs specific to the pull:
    /// <list type="bullet">
    ///   <item><c>Replace</c> — drop the existing rows for that date, then add the pull.</item>
    ///   <item><c>Append</c> — the prior unconditional behavior; add alongside (duplicates
    ///   are an accepted, explicit choice).</item>
    ///   <item><c>Cancel</c> — do nothing. Also the default for window-close / Esc.</item>
    /// </list>
    /// </summary>
    public enum PullCollisionAction
    {
        Replace,
        Append,
        Cancel
    }

    /// <summary>
    /// Pure, WPF-free predicate backing the box-set wizard's pull-collision prompt. Kept out
    /// of the code-behind so the collision decision is unit-testable in isolation (mirrors the
    /// <see cref="BoxSetGroupHeader.IsActiveGroup"/> pure-helper pattern).
    /// </summary>
    public static class BoxSetPullCollision
    {
        /// <summary>
        /// True if any track in <paramref name="tracks"/> carries the given
        /// <paramref name="date"/>. Uses the same exact-string identity as the wizard's date
        /// grouping (<c>PropertyGroupDescription(nameof(BoxSetTrack.Date))</c>) so that an
        /// empty <paramref name="date"/> matches the no-date group's rows the same way the grid
        /// groups them. Ordinal comparison; null-safe on both the sequence and individual rows.
        /// </summary>
        public static bool HasTracksForDate(IEnumerable<BoxSetTrack>? tracks, string date)
        {
            if (tracks == null)
                return false;

            return tracks.Any(t => string.Equals(t?.Date ?? "", date ?? "", StringComparison.Ordinal));
        }
    }
}
