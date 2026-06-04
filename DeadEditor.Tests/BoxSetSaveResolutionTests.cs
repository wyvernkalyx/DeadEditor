using DeadEditor.Helpers;
using Xunit;

namespace DeadEditor.Tests
{
    /// <summary>
    /// Coverage for <see cref="BoxSetSaveResolution.Resolve"/> — the pure save-identity
    /// decision. Exercises every row of the four-outcome table (new vs. editing,
    /// slug changed vs. unchanged, target taken vs. free) plus the empty-original and
    /// ordinal-comparison edges.
    /// </summary>
    public class BoxSetSaveResolutionTests
    {
        [Fact]
        public void NewBox_FreeName_SaveNew()
        {
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: null, newSlug: "europe-72", targetSlugExists: false);
            Assert.Equal(BoxSetSaveOutcome.SaveNew, outcome);
        }

        [Fact]
        public void NewBox_TakenName_NameCollision()
        {
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: null, newSlug: "europe-72", targetSlugExists: true);
            Assert.Equal(BoxSetSaveOutcome.NameCollision, outcome);
        }

        [Fact]
        public void Edit_NameUnchanged_Overwrite_EvenThoughTargetExists()
        {
            // slug == original: the "existing" file is the box itself, not a collision.
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: "europe-72", newSlug: "europe-72", targetSlugExists: true);
            Assert.Equal(BoxSetSaveOutcome.Overwrite, outcome);
        }

        [Fact]
        public void Edit_NameChanged_NewSlugFree_MoveRename()
        {
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: "europe-72", newSlug: "europe-72-deluxe", targetSlugExists: false);
            Assert.Equal(BoxSetSaveOutcome.MoveRename, outcome);
        }

        [Fact]
        public void Edit_NameChanged_NewSlugTaken_NameCollision()
        {
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: "europe-72", newSlug: "so-many-roads", targetSlugExists: true);
            Assert.Equal(BoxSetSaveOutcome.NameCollision, outcome);
        }

        [Fact]
        public void EmptyOriginal_TreatedAsNew_NotEditing()
        {
            // Empty string is the no-original (new box) case, same as null.
            var free = BoxSetSaveResolution.Resolve(originalSlug: "", newSlug: "europe-72", targetSlugExists: false);
            Assert.Equal(BoxSetSaveOutcome.SaveNew, free);

            var taken = BoxSetSaveResolution.Resolve(originalSlug: "", newSlug: "europe-72", targetSlugExists: true);
            Assert.Equal(BoxSetSaveOutcome.NameCollision, taken);
        }

        [Fact]
        public void SlugComparison_IsOrdinalCaseSensitive()
        {
            // A case-only difference from the original slug is NOT "unchanged" — so with the
            // target free this is a rename, not an overwrite. (DeriveSlug lowercases, so this
            // guards the explicit ordinal comparison rather than a real-world name case.)
            var outcome = BoxSetSaveResolution.Resolve(originalSlug: "Europe-72", newSlug: "europe-72", targetSlugExists: false);
            Assert.Equal(BoxSetSaveOutcome.MoveRename, outcome);
        }
    }
}
