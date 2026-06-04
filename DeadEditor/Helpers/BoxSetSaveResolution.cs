using System;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// The four mutually-exclusive outcomes of resolving a box-set save into an
    /// identity decision (overwrite / rename / collision). Computed purely by
    /// <see cref="BoxSetSaveResolution.Resolve"/>; the actual file I/O for
    /// <see cref="Overwrite"/> and <see cref="MoveRename"/> is the wizard's job.
    /// </summary>
    public enum BoxSetSaveOutcome
    {
        /// <summary>Brand-new box, name free — write the file for the first time.</summary>
        SaveNew,

        /// <summary>Editing, name unchanged (slug == original) — overwrite the box's own file.</summary>
        Overwrite,

        /// <summary>Editing, name changed to a free slug — write the new file, then delete the old.</summary>
        MoveRename,

        /// <summary>The target slug already belongs to a <em>different</em> box — refuse, ask for a new name.</summary>
        NameCollision
    }

    /// <summary>
    /// Pure, WPF-free, I/O-free resolution of the box-set save-identity decision. Kept out of
    /// the wizard code-behind so the four-outcome rule is unit-testable in isolation (mirrors the
    /// <see cref="BoxSetPullCollision"/> / <see cref="BoxSetTrackMutations"/> pure-helper pattern).
    ///
    /// The slug is the identity (one <c>&lt;slug&gt;.json</c> file per box, slug derived from the
    /// name via <c>BoxSetService.DeriveSlug</c>). A brand-new box is simply the "edit with no
    /// original" case. See documentation/box-set-design-memo.md (edit-mode, ~:202).
    /// </summary>
    public static class BoxSetSaveResolution
    {
        /// <summary>
        /// Resolves a save into one of the four <see cref="BoxSetSaveOutcome"/> values.
        /// </summary>
        /// <param name="originalSlug">
        /// The slug the box was loaded from when editing; <c>null</c> or empty for a brand-new box.
        /// </param>
        /// <param name="newSlug">
        /// The slug derived from the (possibly edited) name at save time
        /// (<c>BoxSetService.DeriveSlug(definition.Name)</c>).
        /// </param>
        /// <param name="targetSlugExists">
        /// Whether a definition file already exists at <paramref name="newSlug"/>.
        /// </param>
        public static BoxSetSaveOutcome Resolve(string? originalSlug, string newSlug, bool targetSlugExists)
        {
            bool editing = !string.IsNullOrEmpty(originalSlug);

            // Slugs are already lowercased by DeriveSlug; compare ordinally and be explicit about it.
            bool slugUnchanged = editing
                && string.Equals(originalSlug, newSlug, StringComparison.Ordinal);

            if (slugUnchanged) return BoxSetSaveOutcome.Overwrite;        // overwrite self — not a collision
            if (targetSlugExists) return BoxSetSaveOutcome.NameCollision; // would clobber a different box
            return editing ? BoxSetSaveOutcome.MoveRename : BoxSetSaveOutcome.SaveNew;
        }
    }
}
