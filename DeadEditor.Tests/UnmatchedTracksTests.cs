using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for the pure <see cref="UnmatchedTracks.Compute"/> selector: the tracks
/// the Match Setlist compute produced no proposal for, recovered by subtracting
/// the proposals' tracks from the full input list. Identity is by reference (the
/// same instances <see cref="SetlistMatcher.Apply"/> writes back through), input
/// order preserved.
/// </summary>
public class UnmatchedTracksTests
{
    [Fact]
    public void AllTracksMatched_ReturnsEmpty()
    {
        var t1 = MakeTrack(1, "Truckin'");
        var t2 = MakeTrack(2, "Sugaree");
        var tracks = new List<TrackInfo> { t1, t2 };
        var set = MakeSet(MakeProposal(t1), MakeProposal(t2));

        var unmatched = UnmatchedTracks.Compute(tracks, set);

        Assert.Empty(unmatched);
    }

    [Fact]
    public void SomeUnmatched_ReturnsNonProposalTracks_InInputOrder()
    {
        var t1 = MakeTrack(1, "Truckin'");
        var t2 = MakeTrack(2, "Unknown Jam");
        var t3 = MakeTrack(3, "Sugaree");
        var t4 = MakeTrack(4, "Tuning");
        var tracks = new List<TrackInfo> { t1, t2, t3, t4 };
        // Only t1 and t3 got proposals; t2 and t4 are unmatched.
        var set = MakeSet(MakeProposal(t1), MakeProposal(t3));

        var unmatched = UnmatchedTracks.Compute(tracks, set);

        Assert.Equal(new[] { t2, t4 }, unmatched);
    }

    [Fact]
    public void EmptyTracks_ReturnsEmpty()
    {
        var unmatched = UnmatchedTracks.Compute(new List<TrackInfo>(), MakeSet());

        Assert.Empty(unmatched);
    }

    [Fact]
    public void Identity_IsByReference_NotValueEquality()
    {
        // A distinct TrackInfo with the SAME field values as the proposal's track
        // is still unmatched: Apply writes back through the proposal's Track
        // instance, so only reference identity counts.
        var proposalTrack = MakeTrack(1, "Truckin'");
        var lookalike = MakeTrack(1, "Truckin'"); // equal fields, different instance
        var tracks = new List<TrackInfo> { lookalike };
        var set = MakeSet(MakeProposal(proposalTrack));

        var unmatched = UnmatchedTracks.Compute(tracks, set);

        var only = Assert.Single(unmatched);
        Assert.Same(lookalike, only);
    }

    // ===== Helpers =====

    private static TrackInfo MakeTrack(int trackNumber, string songName)
        => new TrackInfo
        {
            FilePath = "/fake/t.flac",
            FileName = "t.flac",
            DiscNumber = 1,
            TrackNumber = trackNumber,
            SongName = songName,
        };

    private static SetlistMatcher.TrackProposal MakeProposal(TrackInfo track)
        => new SetlistMatcher.TrackProposal
        {
            Track = track,
            OldSongName = track.SongName,
            NewSongName = track.SongName,
            OldSegue = false,
            NewSegue = false,
            CoveredEntryIndices = new List<int> { 0 },
        };

    private static SetlistMatcher.ProposalSet MakeSet(params SetlistMatcher.TrackProposal[] proposals)
        => new SetlistMatcher.ProposalSet
        {
            Proposals = proposals.ToList(),
            ClaimedPositions = new HashSet<int>(),
        };
}
