using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Rules tests for <see cref="MetadataValidator"/>: duplicates within a disc,
/// zero values, and gap detection (with 100-format awareness).
///
/// The validator is informational; false positives lead to a warning the user
/// can ignore, false negatives lead to a missed warning. Both are recoverable.
/// </summary>
public class MetadataValidatorTests
{
    [Fact]
    public void CleanSingleDisc_PlainFormat_NoIssues()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 2, 3, 4, 5 });
        var issues = MetadataValidator.Validate(tracks);
        Assert.Empty(issues);
    }

    [Fact]
    public void CleanMultiDisc_HundredFormat_NoIssues()
    {
        var tracks = new List<TrackInfo>();
        tracks.AddRange(MakeTracks(disc: 1, numbers: new[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 }));
        tracks.AddRange(MakeTracks(disc: 2, numbers: new[] { 201, 202, 203, 204, 205, 206, 207, 208 }));

        var issues = MetadataValidator.Validate(tracks);
        Assert.Empty(issues);
    }

    [Fact]
    public void DuplicateWithinDisc_OneIssueNamesNumberAndDisc()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 2, 3, 3, 5 });
        var issues = MetadataValidator.Validate(tracks);

        Assert.Contains(issues, msg => msg.Contains("Disc 1") && msg.Contains("3"));
    }

    [Fact]
    public void ZeroTrackNumber_FlagsAffectedTrack()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 0, 3 });
        tracks[1].SongName = "Sugar Magnolia";

        var issues = MetadataValidator.Validate(tracks);

        Assert.Contains(issues, msg => msg.Contains("Sugar Magnolia") && msg.Contains("no track number"));
    }

    [Fact]
    public void ZeroDiscNumber_FlagsAffectedTrack()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 2 });
        tracks[0].DiscNumber = 0;
        tracks[0].SongName = "Cosmic Charlie";

        var issues = MetadataValidator.Validate(tracks);

        Assert.Contains(issues, msg => msg.Contains("Cosmic Charlie") && msg.Contains("no disc number"));
    }

    [Fact]
    public void GapInPlainFormat_OneIssueNamesTheJump()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 2, 4 });
        var issues = MetadataValidator.Validate(tracks);

        Assert.Single(issues);
        Assert.Contains("Disc 1", issues[0]);
        Assert.Contains("track 2", issues[0]);
        Assert.Contains("track 4", issues[0]);
    }

    [Fact]
    public void GapInHundredFormat_OneIssueNamesTheJump()
    {
        var tracks = MakeTracks(disc: 1, numbers: new[] { 101, 102, 104 });
        var issues = MetadataValidator.Validate(tracks);

        Assert.Single(issues);
        Assert.Contains("track 102", issues[0]);
        Assert.Contains("track 104", issues[0]);
    }

    [Fact]
    public void MixedFormatsAcrossDiscs_NoIssues()
    {
        // Disc 1 plain (1..5), disc 2 hundred-format (201..205). Each disc detects
        // its own format independently — no false positive on either.
        var tracks = new List<TrackInfo>();
        tracks.AddRange(MakeTracks(disc: 1, numbers: new[] { 1, 2, 3, 4, 5 }));
        tracks.AddRange(MakeTracks(disc: 2, numbers: new[] { 201, 202, 203, 204, 205 }));

        var issues = MetadataValidator.Validate(tracks);
        Assert.Empty(issues);
    }

    [Fact]
    public void MultipleIssues_DuplicatePlusGapPlusZero_AllReported()
    {
        // Disc 1: track #2 is duplicated, gap from 2 to 5, and one track has zero.
        var tracks = MakeTracks(disc: 1, numbers: new[] { 1, 2, 2, 5 });
        var zeroTrack = new TrackInfo
        {
            FilePath = "/fake/zero.flac",
            FileName = "zero.flac",
            DiscNumber = 1,
            TrackNumber = 0,
            SongName = "Mystery Track",
        };
        tracks.Add(zeroTrack);

        var issues = MetadataValidator.Validate(tracks);

        Assert.Contains(issues, m => m.Contains("Mystery Track") && m.Contains("no track number"));
        Assert.Contains(issues, m => m.Contains("Disc 1") && m.Contains("two tracks numbered 2"));
        Assert.Contains(issues, m => m.Contains("Disc 1") && m.Contains("track 2") && m.Contains("track 5"));
    }

    [Fact]
    public void DavesPicks43Style_GapFromBaseInHundredFormat()
    {
        // 102..107 on disc 1 with no track 101 — the motivating use case.
        var tracks = MakeTracks(disc: 1, numbers: new[] { 102, 103, 104, 105, 106, 107 });
        var issues = MetadataValidator.Validate(tracks);

        Assert.Single(issues);
        Assert.Contains("Disc 1", issues[0]);
        Assert.Contains("102", issues[0]);
        Assert.Contains("101", issues[0]);
    }

    [Fact]
    public void EmptyTrackList_NoIssues()
    {
        var issues = MetadataValidator.Validate(new List<TrackInfo>());
        Assert.Empty(issues);
    }

    // ===== TrackNumber clamp test (model layer) =====

    [Fact]
    public void TrackNumberSetter_ClampsNegativeToZero()
    {
        var t = new TrackInfo { TrackNumber = 5 };
        t.TrackNumber = -3;
        Assert.Equal(0, t.TrackNumber);
    }

    [Fact]
    public void DiscNumberSetter_ClampsNegativeToZero()
    {
        var t = new TrackInfo { DiscNumber = 1 };
        t.DiscNumber = -7;
        Assert.Equal(0, t.DiscNumber);
    }

    // ===== Helpers =====

    private static List<TrackInfo> MakeTracks(int disc, IEnumerable<int> numbers)
    {
        var list = new List<TrackInfo>();
        foreach (var n in numbers)
        {
            list.Add(new TrackInfo
            {
                FilePath = $"/fake/d{disc}_t{n}.flac",
                FileName = $"d{disc}_t{n}.flac",
                DiscNumber = disc,
                TrackNumber = n,
                SongName = $"Song {disc}-{n}",
            });
        }
        return list;
    }
}
