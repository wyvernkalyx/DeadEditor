using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="ReviewDefaultPolicy"/> — the per-field defaults from
/// review-surface spec §5. Both segue directions (add and remove) default to
/// Accept (owner-ratified unification): the mandatory review step, not a
/// pre-checked default, is the guard against silent segue loss. The segue Accept
/// checkbox carries a single uniform label in either direction.
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
    public void Segue_Remove_TrueToFalse_DefaultsAccept()
    {
        // Unified default (spec §5, owner-ratified): remove now defaults Accept too —
        // the review step, not a pre-checked default, guards against silent segue loss.
        Assert.Equal(ReviewDecision.Accept,
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
    public void SegueAcceptLabel_IsUniform_AcceptSetlistOverMedia()
    {
        // One label for both directions (spec §5): the row's Media/Setlist columns
        // carry the direction, so the checkbox reads uniformly.
        Assert.Equal("Accept setlist over media", ReviewDefaultPolicy.SegueAcceptLabel);
    }
}
