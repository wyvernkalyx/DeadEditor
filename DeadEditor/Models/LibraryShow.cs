using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Models
{
    /// <summary>
    /// LibraryShow - Represents a concert/album in the library browser
    /// Supports audience recordings, official releases, and studio albums
    /// </summary>
    public class LibraryShow
    {
        // Album type (defaults to Audience Recording)
        public AlbumType Type { get; set; } = AlbumType.AudienceRecording;

        // Live recording properties
        public string Date { get; set; } = "";
        public string Venue { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Location { get; set; } = "";
        public string OfficialRelease { get; set; } = "";

        // Multi-date/multi-venue support for official releases
        public List<string> ContainsDates { get; set; } = new List<string>();
        public List<string> ContainsVenues { get; set; } = new List<string>();

        // Studio album properties
        public string AlbumName { get; set; } = "";
        public int? ReleaseYear { get; set; }
        public string Edition { get; set; } = "";

        // Common properties
        public int TrackCount { get; set; }
        public List<string> FolderPaths { get; set; } = new List<string>();

        // Backward-compatible property for code not yet updated
        public string FolderPath
        {
            get => FolderPaths.FirstOrDefault() ?? "";
            set
            {
                if (FolderPaths.Count == 0)
                    FolderPaths.Add(value);
                else
                    FolderPaths[0] = value;
            }
        }

        // Smart display properties that adapt based on type
        public string TypeIcon => Type == AlbumType.OfficialRelease ? "📀" : "🎸";

        public string PrimaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (!string.IsNullOrEmpty(OfficialRelease) ? OfficialRelease : AlbumName)
                : Date;

        public string SecondaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (ContainsDates.Count > 1 ? string.Join(", ", ContainsDates) : (ReleaseYear.HasValue ? ReleaseYear.Value.ToString() : Date))
                : Venue;

        public string TertiaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (ContainsVenues.Count > 1 ? string.Join(", ", ContainsVenues) : Edition)
                : Location;
    }
}
