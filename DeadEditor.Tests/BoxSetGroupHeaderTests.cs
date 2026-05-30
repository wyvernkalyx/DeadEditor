using DeadEditor.Helpers;
using Xunit;

namespace DeadEditor.Tests
{
    public class BoxSetGroupHeaderTests
    {
        [Fact]
        public void FormatGroupHeader_DateAndVenue_IncludesBoth()
        {
            var result = BoxSetGroupHeader.FormatGroupHeader(
                "1987-12-27", "Long Beach Arena, Long Beach, CA", 21);
            Assert.Equal("1987-12-27 — Long Beach Arena, Long Beach, CA (21 tracks)", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void FormatGroupHeader_DateNoVenue_OmitsVenue(string? venue)
        {
            var result = BoxSetGroupHeader.FormatGroupHeader("1987-12-27", venue, 21);
            Assert.Equal("1987-12-27 (21 tracks)", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FormatGroupHeader_BlankDate_ShowsNoDate(string? date)
        {
            var result = BoxSetGroupHeader.FormatGroupHeader(date, "Some Venue", 3);
            Assert.Equal("(no date) (3 tracks)", result);
        }

        [Fact]
        public void FormatGroupHeader_SingleTrack_IsSingular()
        {
            var result = BoxSetGroupHeader.FormatGroupHeader("1972-05-04", null, 1);
            Assert.Equal("1972-05-04 (1 track)", result);
        }

        [Fact]
        public void FormatGroupHeader_BlankDateSingleTrack_IsSingular()
        {
            var result = BoxSetGroupHeader.FormatGroupHeader("", null, 1);
            Assert.Equal("(no date) (1 track)", result);
        }
    }
}
