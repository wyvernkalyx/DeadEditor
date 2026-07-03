using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="ReviewDefaultPolicy"/> — the protective per-field
/// defaults from review-surface spec §5. The one asymmetry that matters: a
/// segue remove (true -> false) defaults to Ignore so a sparse source cannot
/// clear a real segue silently.
/// </summary>
public class ReviewDefaultPolicyTests
{
    [Fact]
    public void SongName_VariantToCanonical_DefaultsAccept()
    {
        Assert.Equal(ReviewDecision.Accept,
            ReviewDefaultPolicy.DefaultForSongName("Watchtower", "All Along The Watchtower"));
    }

    [Fact]
    public void Segue_Add_FalseToTrue_DefaultsAccept()
    {
        Assert.Equal(ReviewDecision.Accept,
            ReviewDefaultPolicy.DefaultForSegue(oldSegue: false, newSegue: true));
    }

    [Fact]
    public void Segue_Remove_TrueToFalse_DefaultsIgnore()
    {
        // The data-loss direction — must default Ignore (spec §5).
        Assert.Equal(ReviewDecision.Ignore,
            ReviewDefaultPolicy.DefaultForSegue(oldSegue: true, newSegue: false));
    }

    [Fact]
    public void Segue_Unchanged_DecisionIrrelevant_ReturnsAccept()
    {
        Assert.Equal(ReviewDecision.Accept,
            ReviewDefaultPolicy.DefaultForSegue(oldSegue: true, newSegue: true));
        Assert.Equal(ReviewDecision.Accept,
            ReviewDefaultPolicy.DefaultForSegue(oldSegue: false, newSegue: false));
    }

    [Fact]
    public void SegueActionLabel_Remove_TrueToFalse_ReadsRemove()
    {
        // Checking the box accepts the setlist value (false) — i.e. removes the segue.
        Assert.Equal("Remove segue",
            ReviewDefaultPolicy.SegueActionLabel(current: true, proposed: false));
    }

    [Fact]
    public void SegueActionLabel_Add_FalseToTrue_ReadsAdd()
    {
        // The direction the gate fixture cannot produce — locked by this test.
        Assert.Equal("Add segue",
            ReviewDefaultPolicy.SegueActionLabel(current: false, proposed: true));
    }
}
