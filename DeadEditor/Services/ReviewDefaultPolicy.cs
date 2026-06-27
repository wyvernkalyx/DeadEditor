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
    }
}
