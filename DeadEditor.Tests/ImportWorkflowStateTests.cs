using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Heuristic tests for <see cref="ImportWorkflowState.Compute"/>. Covers each
/// stage transition and the Skipped/Current derivation. Heuristics are
/// intentionally cheap; these tests pin the specific expected behavior so that
/// later refinements stay deliberate.
/// </summary>
public class ImportWorkflowStateTests
{
    // Tiny canonical "song database" — keeps the Structure heuristic deterministic
    // without loading songs.json.
    private static readonly HashSet<string> CanonicalSongs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Eyes of the World",
        "Sugar Magnolia",
        "Scarlet Begonias",
        "Fire on the Mountain",
        "China Cat Sunflower",
        "I Know You Rider",
        "Dark Star",
        "Truckin'",
        "Casey Jones",
        "Friend of the Devil",
    };

    private static Func<string, bool> CanonicalCheck =>
        name => CanonicalSongs.Contains(name);

    [Fact]
    public void NoFolderLoaded_LoadIsCurrent_OthersUpcoming()
    {
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = Array.Empty<string>(),
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.Equal(5, stages.Count);

        var load = stages.Single(s => s.Name == ImportWorkflowState.LoadName);
        Assert.True(load.IsCurrent);
        Assert.False(load.IsCompleted);

        // No tracks → none of the heuristics depending on tracks can fire.
        Assert.All(stages.Where(s => s.Name != ImportWorkflowState.LoadName),
            s => Assert.True(s.IsUpcoming, $"Expected '{s.Name}' to be Upcoming"));
    }

    [Fact]
    public void FolderLoaded_NothingElse_LoadCompleted_EnrichCurrent()
    {
        // Tracks present, but: no MBID, titles still have date suffix, not canonical,
        // not under library root.
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World, 5/8/77",
                "Scarlet Begonias, 5/8/77",
            },
            MbidPresent = false,
            CurrentFolderPath = @"C:\source\bootleg-1977",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        var byName = stages.ToDictionary(s => s.Name);

        Assert.True(byName[ImportWorkflowState.LoadName].IsCompleted);
        Assert.True(byName[ImportWorkflowState.EnrichName].IsCurrent);
        Assert.True(byName[ImportWorkflowState.CleanName].IsUpcoming);
        Assert.True(byName[ImportWorkflowState.StructureName].IsUpcoming);
        Assert.True(byName[ImportWorkflowState.ImportName].IsUpcoming);
    }

    [Fact]
    public void OutOfOrder_ImportedAlbumWithCanonicalTitlesButNoMbid_FlagsEnrichSkipped()
    {
        // Folder is under LibraryRoot (Import done), titles canonical (Structure +
        // Clean done), but MBID never applied. Enrich should be Skipped because a
        // later stage (Structure / Import) is Completed.
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World",
                "Sugar Magnolia",
            },
            MbidPresent = false,
            CurrentFolderPath = @"C:\library\Grateful Dead\1977-05-08",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        var byName = stages.ToDictionary(s => s.Name);

        Assert.True(byName[ImportWorkflowState.LoadName].IsCompleted);
        Assert.True(byName[ImportWorkflowState.EnrichName].IsSkipped);
        Assert.True(byName[ImportWorkflowState.CleanName].IsCompleted);
        Assert.True(byName[ImportWorkflowState.StructureName].IsCompleted);
        Assert.True(byName[ImportWorkflowState.ImportName].IsCompleted);

        // No Current when all later stages have wrapped past.
        Assert.DoesNotContain(stages, s => s.IsCurrent);
    }

    [Fact]
    public void ImportedAlbumReopened_AllUnderLibraryRoot_ImportCompleted()
    {
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[] { "Eyes of the World" },
            MbidPresent = true,
            CurrentFolderPath = @"C:\library\Grateful Dead\1977-05-08",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        var import = stages.Single(s => s.Name == ImportWorkflowState.ImportName);
        Assert.True(import.IsCompleted);
        // All stages completed → no Current.
        Assert.DoesNotContain(stages, s => s.IsCurrent);
    }

    [Fact]
    public void FullyComplete_AllCompleted_NoCurrent()
    {
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World",
                "Sugar Magnolia",
                "Scarlet Begonias",
                "Fire on the Mountain",
            },
            MbidPresent = true,
            CurrentFolderPath = @"C:\library\Grateful Dead\1977-05-08",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.All(stages, s => Assert.True(s.IsCompleted, $"Expected '{s.Name}' to be Completed"));
        Assert.DoesNotContain(stages, s => s.IsCurrent);
        Assert.DoesNotContain(stages, s => s.IsSkipped);
    }

    [Fact]
    public void EnrichDone_TitlesNotYetCleaned_CleanIsCurrent()
    {
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World, 5/8/77",
                "Scarlet Begonias, 5/8/77",
            },
            MbidPresent = true,
            CurrentFolderPath = @"C:\source\bootleg-1977",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        var byName = stages.ToDictionary(s => s.Name);
        Assert.True(byName[ImportWorkflowState.EnrichName].IsCompleted);
        Assert.True(byName[ImportWorkflowState.CleanName].IsCurrent);
    }

    [Fact]
    public void CleanHeuristic_FlagsParenthesizedDateSuffix()
    {
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World (1977-05-08)",
            },
            MbidPresent = true,
            CurrentFolderPath = @"C:\source\bootleg",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.False(stages.Single(s => s.Name == ImportWorkflowState.CleanName).IsCompleted);
    }

    [Fact]
    public void StructureHeuristic_BelowEightyPercent_NotCompleted()
    {
        // 3 of 5 canonical = 60%. Under 80% threshold.
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World",
                "Sugar Magnolia",
                "Scarlet Begonias",
                "Random Jam #1",
                "Drumz",
            },
            MbidPresent = true,
            CurrentFolderPath = @"C:\source\bootleg",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.False(stages.Single(s => s.Name == ImportWorkflowState.StructureName).IsCompleted);
    }

    [Fact]
    public void StructureHeuristic_AtEightyPercent_Completed()
    {
        // 4 of 5 canonical = 80%. At the threshold (>=).
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[]
            {
                "Eyes of the World",
                "Sugar Magnolia",
                "Scarlet Begonias",
                "Fire on the Mountain",
                "Random Jam #1",
            },
            MbidPresent = true,
            CurrentFolderPath = @"C:\source\bootleg",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.True(stages.Single(s => s.Name == ImportWorkflowState.StructureName).IsCompleted);
    }

    [Fact]
    public void ImportStage_UsesPathGuardSemantics_SiblingPrefixDoesNotMatch()
    {
        // Folder C:\library2\... must NOT match library root C:\library.
        // This is the regression PathGuard's trailing-separator normalization
        // protects against; the Import stage piggybacks on that contract.
        var stages = ImportWorkflowState.Compute(new ImportWorkflowInput
        {
            TrackTitles = new[] { "Eyes of the World" },
            MbidPresent = false,
            CurrentFolderPath = @"C:\library2\Grateful Dead\1977-05-08",
            LibraryRootPath = @"C:\library",
            IsCanonicalSongName = CanonicalCheck,
        });

        Assert.False(stages.Single(s => s.Name == ImportWorkflowState.ImportName).IsCompleted);
    }
}
