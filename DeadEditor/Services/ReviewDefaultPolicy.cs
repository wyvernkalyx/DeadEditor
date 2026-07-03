using System;

namespace DeadEditor.Services
{
    /// <summary>
    /// Per-field resolution for one proposed Match Setlist change: take the
    /// new value (<see cref="Accept"/>) or keep the existing one
    /// (<see cref="Ignore"/>). The Match Setlist review surface resolves each
    /// field independently (spec §4), so a single track can canonicalize its
    /// name while leaving its segue untouched.
    /// </summary>
    public enum ReviewDecision
    {
        Accept,
        Ignore,
    }

    /// <summary>
    /// The protective per-field defaults from the review-surface spec §5.
    /// Pure and dependency-free: each method maps an old-to-new change to the
    /// decision a row should be pre-seeded with.
    ///
    /// The one asymmetry that matters is segue <b>remove</b> (true -> false):
    /// the dataset's documented failure mode is segue under-capture, so a
    /// sparse source clearing a real segue is the one direction that must
    /// never apply silently — it defaults to <see cref="ReviewDecision.Ignore"/>.
    /// </summary>
    public static class ReviewDefaultPolicy
    {
        /// <summary>
        /// SongName default: a real variant -> canonical change defaults to
        /// Accept (spec §5). When old == new (Ordinal) the field is a no-op and
        /// the decision is irrelevant — Accept is returned because the
        /// effective value is the same either way.
        /// </summary>
        public static ReviewDecision DefaultForSongName(string oldName, string newName)
        {
            // No-op or variant->canonical both default Accept; the no-op case is
            // decision-irrelevant (Effective == Old == New regardless).
            return ReviewDecision.Accept;
        }

        /// <summary>
        /// Segue default (spec §5 table):
        /// add (false -> true) = Accept; remove (true -> false) = Ignore (the
        /// data-loss direction); unchanged = decision irrelevant (Accept).
        /// </summary>
        public static ReviewDecision DefaultForSegue(bool oldSegue, bool newSegue)
        {
            if (oldSegue && !newSegue)
            {
                // Remove: the protected direction. Default to keeping the real
                // segue rather than letting a sparse source clear it silently.
                return ReviewDecision.Ignore;
            }

            // Add (false -> true) and unchanged both default Accept; unchanged is
            // decision-irrelevant (Effective == Old == New regardless).
            return ReviewDecision.Accept;
        }

        /// <summary>
        /// The label for the segue Accept checkbox, describing what CHECKING does
        /// (checking = accept the setlist value = set segue to <paramref name="proposed"/>).
        /// A static "Accept segue" mis-reads in the remove direction, so the label is dynamic:
        /// remove (true -> false) = "Remove segue"; add (false -> true) = "Add segue". Pure so both
        /// directions are unit-testable (the add direction has no gate fixture).
        /// </summary>
        public static string SegueActionLabel(bool current, bool proposed)
        {
            if (current && !proposed) return "Remove segue";
            if (!current && proposed) return "Add segue";
            // Unchanged: the segue sub-block is not shown in this case (SegueChanged is false), so
            // this is never displayed; return the proposed-state action for a total function.
            return proposed ? "Add segue" : "Remove segue";
        }
    }
}
