using System.Linq;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="TitleStructureParser"/>. Covers every shape from the Y2K-2a
/// inventory plus canonical-paren preservation, edge cases, and the embedded-segue rule.
/// 50 tests across 25 categories. See documentation/title-structure-parser-spec.md.
/// </summary>
public class TitleStructureParserTests
{
    // === Category 1: Bare titles ===

    [Fact]
    public void Bare_PlainTitle_ReturnsSongNameOnly()
    {
        var r = TitleStructureParser.Parse("Dark Star");
        Assert.Equal("Dark Star", r.SongName);
        Assert.False(r.HasSegue);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
        Assert.Empty(r.RawMetadataFragments);
    }

    [Fact]
    public void Bare_EmptyString_ReturnsEmptyResult()
    {
        var r = TitleStructureParser.Parse("");
        Assert.Equal("", r.SongName);
        Assert.False(r.HasSegue);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
        Assert.Empty(r.RawMetadataFragments);
    }

    [Fact]
    public void Bare_WhitespaceOnly_ReturnsEmptyResult()
    {
        var r = TitleStructureParser.Parse("   ");
        Assert.Equal("", r.SongName);
        Assert.False(r.HasSegue);
    }

    [Fact]
    public void Bare_NullInput_ReturnsEmptyResult()
    {
        var r = TitleStructureParser.Parse(null);
        Assert.Equal("", r.SongName);
        Assert.False(r.HasSegue);
    }

    // === Category 2: Date-only metadata parens ===

    [Theory]
    [InlineData("Dark Star (1969-12-26)", "Dark Star", "1969-12-26")]
    [InlineData("Dark Star (12/26/69)", "Dark Star", "1969-12-26")]
    [InlineData("Dark Star (12/26/1969)", "Dark Star", "1969-12-26")]
    [InlineData("Drums (1971/07/02)", "Drums", "1971-07-02")]
    [InlineData("Bertha [1971-04-27]", "Bertha", "1971-04-27")]
    public void DateOnly_MetadataParen_ExtractsDate(string input, string expectedName, string expectedDate)
    {
        var r = TitleStructureParser.Parse(input);
        Assert.Equal(expectedName, r.SongName);
        Assert.Equal(expectedDate, r.TrackDate);
        Assert.Null(r.Venue);
        Assert.False(r.HasSegue);
    }

    // === Category 3: Venue-first parens (the unblocking case) ===

    [Fact]
    public void VenueFirst_CityCommaUsSlash()
    {
        var r = TitleStructureParser.Parse("Cold Rain And Snow (San Francisco, 11/2/69)");
        Assert.Equal("Cold Rain And Snow", r.SongName);
        Assert.Equal("1969-11-02", r.TrackDate);
        Assert.Equal("San Francisco", r.Venue);
    }

    [Fact]
    public void VenueFirst_CityStateCommaUsSlash()
    {
        var r = TitleStructureParser.Parse("Bertha (Boston, MA, 11/2/69)");
        Assert.Equal("Bertha", r.SongName);
        Assert.Equal("1969-11-02", r.TrackDate);
        Assert.Equal("Boston, MA", r.Venue);
    }

    [Fact]
    public void VenueFirst_VenueCommaUsSlash()
    {
        var r = TitleStructureParser.Parse("Sugar Magnolia (Fox Theatre, 10/18/72)");
        Assert.Equal("Sugar Magnolia", r.SongName);
        Assert.Equal("1972-10-18", r.TrackDate);
        Assert.Equal("Fox Theatre", r.Venue);
    }

    // === Category 4: Date-first parens ===

    [Fact]
    public void DateFirst_UsSlashThenVenue()
    {
        var r = TitleStructureParser.Parse("Sugar Magnolia (10/18/72 Fox Theatre)");
        Assert.Equal("Sugar Magnolia", r.SongName);
        Assert.Equal("1972-10-18", r.TrackDate);
        Assert.Equal("Fox Theatre", r.Venue);
    }

    [Fact]
    public void DateFirst_YearFirstSlashThenVenue()
    {
        var r = TitleStructureParser.Parse("Drums (1971/07/02 Filmore West)");
        Assert.Equal("Drums", r.SongName);
        Assert.Equal("1971-07-02", r.TrackDate);
        Assert.Equal("Filmore West", r.Venue);
    }

    // === Category 5: Bracketed metadata ===

    [Fact]
    public void Bracketed_DateThenVenue()
    {
        var r = TitleStructureParser.Parse("Bertha [12/31/69 Winterland]");
        Assert.Equal("Bertha", r.SongName);
        Assert.Equal("1969-12-31", r.TrackDate);
        Assert.Equal("Winterland", r.Venue);
    }

    [Fact]
    public void Bracketed_VenueCityStateThenDate()
    {
        var r = TitleStructureParser.Parse("Promised Land [Kiel Opera House, St. Louis, MO 12/9/71]");
        Assert.Equal("Promised Land", r.SongName);
        Assert.Equal("1971-12-09", r.TrackDate);
        Assert.Equal("Kiel Opera House, St. Louis, MO", r.Venue);
    }

    [Fact]
    public void Bracketed_DateThenVenueCommaCityState()
    {
        var r = TitleStructureParser.Parse("Bertha [12/31/71, Winterland Arena, San Francisco, CA]");
        Assert.Equal("Bertha", r.SongName);
        Assert.Equal("1971-12-31", r.TrackDate);
        Assert.Equal("Winterland Arena, San Francisco, CA", r.Venue);
    }

    // === Category 6: Live-at markers ===

    [Fact]
    public void LiveAt_ParenForm()
    {
        var r = TitleStructureParser.Parse("Ripple (Live at the Capitol Theatre, Port Chester, NY 2/21/1971)");
        Assert.Equal("Ripple", r.SongName);
        Assert.Equal("1971-02-21", r.TrackDate);
        Assert.Equal("the Capitol Theatre, Port Chester, NY", r.Venue);
    }

    [Fact]
    public void LiveIn_ParenForm()
    {
        var r = TitleStructureParser.Parse("Terrapin Station (Live in Chicago, 1/31/1978)");
        Assert.Equal("Terrapin Station", r.SongName);
        Assert.Equal("1978-01-31", r.TrackDate);
        Assert.Equal("Chicago", r.Venue);
    }

    [Fact]
    public void LiveAt_BracketForm()
    {
        var r = TitleStructureParser.Parse("Ripple [Live at the Capitol Theatre, Port Chester, NY 2/21/1971]");
        Assert.Equal("Ripple", r.SongName);
        Assert.Equal("1971-02-21", r.TrackDate);
        Assert.Equal("the Capitol Theatre, Port Chester, NY", r.Venue);
    }

    // === Category 7: Filler markers ===

    [Fact]
    public void Filler_DashSeparatedVenue()
    {
        var r = TitleStructureParser.Parse("Drums (Filler: 1972-05-04 - Some Venue)");
        Assert.Equal("Drums", r.SongName);
        Assert.Equal("1972-05-04", r.TrackDate);
        Assert.Equal("Some Venue", r.Venue);
    }

    // === Category 8: Editorial markers (Remaster, Reprise, Live) ===

    [Fact]
    public void Remaster_Paren()
    {
        var r = TitleStructureParser.Parse("Ripple (2020 Remaster)");
        Assert.Equal("Ripple", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    [Fact]
    public void Remaster_Bracket()
    {
        var r = TitleStructureParser.Parse("Ripple [2020 Remaster]");
        Assert.Equal("Ripple", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    [Fact]
    public void Reprise_Paren()
    {
        var r = TitleStructureParser.Parse("Not Fade Away (Reprise)");
        Assert.Equal("Not Fade Away", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    [Fact]
    public void StandaloneLive_Paren()
    {
        var r = TitleStructureParser.Parse("Ripple (Live)");
        Assert.Equal("Ripple", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    // === Category 9: Year-tour suffix ===

    [Fact]
    public void YearTour_StrippedFromEnd()
    {
        var r = TitleStructureParser.Parse("Good Lovin' (1972 - Europe '72)");
        Assert.Equal("Good Lovin'", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    [Fact]
    public void YearTour_AfterCanonicalParen_PreservesCanonical()
    {
        var r = TitleStructureParser.Parse("The Stranger (Two Souls in Communion) (1972 - Europe '72)");
        Assert.Equal("The Stranger (Two Souls in Communion)", r.SongName);
        Assert.Null(r.TrackDate);
    }

    // === Category 10: Trailing segue markers ===

    [Theory]
    [InlineData("Dark Star >")]
    [InlineData("Dark Star ->")]
    [InlineData("Dark Star –>")]
    [InlineData("Dark Star →")]
    [InlineData("Dark Star [>]")]
    public void Trailing_SegueMarker_Stripped_HasSegueTrue(string input)
    {
        var r = TitleStructureParser.Parse(input);
        Assert.Equal("Dark Star", r.SongName);
        Assert.True(r.HasSegue);
        Assert.Null(r.TrackDate);
    }

    // === Category 11: Embedded segue marker (mid-title, known limitation) ===

    [Fact]
    public void Embedded_SegueMarker_Preserved_HasSegueTrue()
    {
        // Per the embedded-segue rule: trailing markers strip, embedded markers stay.
        // SongName, segue marker, and second song name preserved verbatim.
        var r = TitleStructureParser.Parse("Dark Star > St. Stephen");
        Assert.Equal("Dark Star > St. Stephen", r.SongName);
        Assert.True(r.HasSegue);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    // === Category 12: Mixed canonical-paren + segue + metadata-paren ===

    [Fact]
    public void Mixed_CanonicalParenSegueAndMetadataParen()
    {
        var r = TitleStructureParser.Parse("Caution (Do Not Stop on Tracks) > (San Francisco, 11/2/69)");
        Assert.Equal("Caution (Do Not Stop on Tracks)", r.SongName);
        Assert.True(r.HasSegue);
        Assert.Equal("1969-11-02", r.TrackDate);
        Assert.Equal("San Francisco", r.Venue);
    }

    // === Category 13: Canonical-paren preservation ===
    // Seven canonical-paren song titles from Data/songs.json must survive untouched.

    [Theory]
    [InlineData("Caution (Do Not Stop on Tracks)")]
    [InlineData("Ain't It Crazy (The Rub)")]
    [InlineData("Man Smart (Woman Smarter)")]
    [InlineData("So Sad (To See Good Love Go Bad)")]
    [InlineData("Golden Road (To Unlimited Devotion)")]
    [InlineData("The Stranger (Two Souls in Communion)")]
    [InlineData("So Sad (To Watch Good Love Go Bad)")]
    public void Canonical_Paren_Preserved(string title)
    {
        var r = TitleStructureParser.Parse(title);
        Assert.Equal(title, r.SongName);
        Assert.False(r.HasSegue);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
        Assert.Empty(r.RawMetadataFragments);
    }

    // === Category 14: Canonical-paren plus metadata-paren ===

    [Fact]
    public void Canonical_PlusMetadataParen_PreservesCanonicalExtractsMetadata()
    {
        var r = TitleStructureParser.Parse("The Stranger (Two Souls in Communion) (1972-05-10)");
        Assert.Equal("The Stranger (Two Souls in Communion)", r.SongName);
        Assert.Equal("1972-05-10", r.TrackDate);
    }

    // === Category 15: Y2K resolution via parser ===

    [Fact]
    public void Y2K_PivotPath_NoAlbumDate_Year69_Maps_1969()
    {
        var r = TitleStructureParser.Parse("Bertha [12/31/69, Venue]");
        Assert.Equal("1969-12-31", r.TrackDate);
    }

    [Fact]
    public void Y2K_AlbumCenturyPath_AlbumDate1969_Year69_Maps_1969()
    {
        var r = TitleStructureParser.Parse("Bertha [12/31/69, Venue]", albumDate: "1969-12-31");
        Assert.Equal("1969-12-31", r.TrackDate);
    }

    // === Category 16: Artist-suffix tail ===

    [Fact]
    public void ArtistSuffix_StrippedFromEnd()
    {
        var r = TitleStructureParser.Parse("Bertha - Grateful Dead__");
        Assert.Equal("Bertha", r.SongName);
        Assert.Null(r.TrackDate);
    }

    [Fact]
    public void MbStyle_FullShape_AllExtracted()
    {
        var r = TitleStructureParser.Parse(
            "Cold Rain And Snow (Live at the Capitol Theatre, Port Chester, NY 2/21/1971) [2020 Remaster] - Grateful Dead__");
        Assert.Equal("Cold Rain And Snow", r.SongName);
        Assert.Equal("1971-02-21", r.TrackDate);
        Assert.Equal("the Capitol Theatre, Port Chester, NY", r.Venue);
        Assert.False(r.HasSegue);
    }

    // === Category 17: Cosmetic prep (//, curly apostrophes, box-drawing dash) ===

    [Fact]
    public void Cosmetic_TapeFlipMarker_Removed()
    {
        var r = TitleStructureParser.Parse("Loser's // tape splice");
        Assert.Equal("Loser's tape splice", r.SongName);
    }

    [Fact]
    public void Cosmetic_CurlyApostrophe_Normalized()
    {
        // U+2019 (RIGHT SINGLE QUOTATION MARK) → U+0027 (APOSTROPHE)
        var r = TitleStructureParser.Parse("Loser’s");
        Assert.Equal("Loser's", r.SongName);
    }

    [Fact]
    public void Cosmetic_BoxDrawingDash_NormalizedToHyphen()
    {
        // U+2500 (BOX DRAWINGS LIGHT HORIZONTAL) → '-'
        var r = TitleStructureParser.Parse("Peggy─O");
        Assert.Equal("Peggy-O", r.SongName);
    }

    // === Category 18: Invalid date in metadata fragment ===

    [Fact]
    public void InvalidDate_FragmentClassifiedMetadata_ButTrackDateNull()
    {
        var r = TitleStructureParser.Parse("Song (13/45/69)");
        Assert.Equal("Song", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
        Assert.Single(r.RawMetadataFragments);
        Assert.Equal("13/45/69", r.RawMetadataFragments[0]);
    }

    // === Category 19: State-code-only metadata ===

    [Fact]
    public void StateCode_BostonMA_ClassifiedMetadata_VenuePreserved()
    {
        var r = TitleStructureParser.Parse("Song (Boston, MA)");
        Assert.Equal("Song", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Equal("Boston, MA", r.Venue);
    }

    // === Category 20: Multiple metadata fragments — first valid date / first venue wins ===

    [Fact]
    public void MultipleMetadataFragments_DateFromSecond_VenueFromFirst()
    {
        var r = TitleStructureParser.Parse("Song (Live at the Fox) (12/9/71)");
        Assert.Equal("Song", r.SongName);
        Assert.Equal("1971-12-09", r.TrackDate);
        Assert.Equal("the Fox", r.Venue);
        Assert.Equal(2, r.RawMetadataFragments.Count);
    }

    // === Category 21: RawMetadataFragments invariant ===

    [Fact]
    public void RawMetadataFragments_NeverNull_EmptyByDefault()
    {
        var r = TitleStructureParser.Parse("Dark Star");
        Assert.NotNull(r.RawMetadataFragments);
        Assert.Empty(r.RawMetadataFragments);
    }

    [Fact]
    public void RawMetadataFragments_CapturesEachMetadataGroupVerbatim()
    {
        var r = TitleStructureParser.Parse("Drums (Filler: 1972-05-04 - Some Venue)");
        Assert.Single(r.RawMetadataFragments);
        Assert.Equal("Filler: 1972-05-04 - Some Venue", r.RawMetadataFragments[0]);
    }

    // === Category 22: Date-extraction priority (ISO > year-first > US-slash) ===

    [Fact]
    public void DatePriority_YearFirstSlash_NotMisreadAsUsSlash()
    {
        // "1971/07/02" must parse as year=1971, m=7, d=2 — not as the substring "71/07/02" via US-slash.
        var r = TitleStructureParser.Parse("Drums (1971/07/02)");
        Assert.Equal("1971-07-02", r.TrackDate);
    }

    // === Category 23: Brackets-as-metadata symmetry with parens ===

    [Fact]
    public void Brackets_RemasterTreatedSameAsParens()
    {
        var r = TitleStructureParser.Parse("Song [Reprise]");
        Assert.Equal("Song", r.SongName);
        Assert.Null(r.TrackDate);
        Assert.Null(r.Venue);
    }

    // === Category 24: Mid-title bracket-segue [>] preserved (embedded) ===

    [Fact]
    public void Embedded_BracketSegue_Preserved_HasSegueTrue()
    {
        var r = TitleStructureParser.Parse("Song [>] Other");
        Assert.Contains("[>]", r.SongName);
        Assert.True(r.HasSegue);
    }

    // === Category 25: Trailing segue + metadata paren combination (already in Cat 12, plus a variant) ===

    [Fact]
    public void TrailingSegueAfterMetadataParen()
    {
        var r = TitleStructureParser.Parse("Help on the Way (1975-08-13) >");
        Assert.Equal("Help on the Way", r.SongName);
        Assert.True(r.HasSegue);
        Assert.Equal("1975-08-13", r.TrackDate);
    }
}
