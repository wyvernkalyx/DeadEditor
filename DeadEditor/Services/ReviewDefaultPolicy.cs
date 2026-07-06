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
    /// The per-field defaults from the review-surface spec §5. Pure and
    /// dependency-free: each method maps an old-to-new change to the decision a
    /// row should be pre-seeded with.
    ///
    /// Both segue directions (add and remove) default to
    /// <see cref="ReviewDecision.Accept"/> (spec §5, owner-ratified). The review
    /// step itself is the consent mechanism — every changed row is shown, and the
    /// per-row Media/Setlist columns make the direction visible — so a pre-checked
    /// default is no longer the guard against silent segue loss. A reviewer keeps a
    /// real segue by unchecking that row; apply semantics are unchanged.
    /// </summary>
    public static class ReviewDefaultPolicy
    {
        /// <summary>
        /// The uniform label for every segue Accept checkbox (spec §5): checking
        /// takes the setlist's value over the media's, in either direction. Matches
        /// the dialog header ("Checked = use the setlist's value"); the row's
        /// Media/Setlist columns already show which direction the change is.
        /// </summary>
        public const string SegueAcceptLabel = "Accept setlist over media";

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
        /// Segue default (spec §5 table): both add (false -> true) and remove
        /// (true -> false) default Accept, as does unchanged (decision-irrelevant,
        /// Effective == Old == New regardless). The remove direction was once
        /// Ignore (the protective asymmetry); the owner ratified unifying it to
        /// Accept now that the review surface makes the apply non-blind. The
        /// parameters are retained for the callsite's clarity and to keep the
        /// unchanged/decision-irrelevant contract explicit.
        /// </summary>
        public static ReviewDecision DefaultForSegue(bool oldSegue, bool newSegue)
        {
            return ReviewDecision.Accept;
        }
    }
}
