using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="TrackInfo.OriginalTitle"/> — the immutable, display-only
/// original title that Track Info's "Original Title" reads.
///
/// The defect it fixes: EditMetadataView.ReconstructRawTitles rewrites
/// <see cref="TrackInfo.RawTitle"/> from the post-match SongName, so Track Info
/// (which used to read RawTitle) showed "Original Title" == "Current Song Name"
/// after a match. OriginalTitle is captured once at load and must survive any
/// later RawTitle rewrite or SongName change.
/// </summary>
public class TrackInfoOriginalTitleTests
{
    [Fact]
    public void OriginalTitle_SurvivesRawTitleRewrite()
    {
        // Simulate load: OriginalTitle captured from the parsed original name.
        var track = new TrackInfo
        {
            SongName = "Good Lovin",
            RawTitle = "Good Lovin",
        };
        track.OriginalTitle = track.SongName;

        // Simulate a match + ReconstructRawTitles: SongName becomes canonical and
        // RawTitle is rewritten from it (the exact rewrite the diagnosis flagged).
        track.SongName = "Good Lovin'";
        track.RawTitle = "Good Lovin' (1977-12-27)";

        // The original is preserved; only RawTitle/SongName moved.
        Assert.Equal("Good Lovin", track.OriginalTitle);
    }

    [Fact]
    public void OriginalTitle_IsSetOnce_LaterWritesIgnored()
    {
        var track = new TrackInfo();

        track.OriginalTitle = "First Original";
        track.OriginalTitle = "Manifest Override";   // e.g. ApplyManifestOverrides

        Assert.Equal("First Original", track.OriginalTitle);
    }

    [Fact]
    public void OriginalTitle_EmptyFirstWrite_DoesNotLock()
    {
        // A null/empty first assignment must not lock the field — the real load
        // value still takes hold on the next non-empty write.
        var track = new TrackInfo();

        track.OriginalTitle = "";
        track.OriginalTitle = "Real Original";

        Assert.Equal("Real Original", track.OriginalTitle);
    }
}
