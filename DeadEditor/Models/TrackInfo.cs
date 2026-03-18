namespace DeadEditor.Models
{
    public class TrackInfo
    {
        public string FilePath { get; set; }           // Full path to FLAC file
        public string FileName { get; set; }           // Just the filename
        public int TrackNumber { get; set; }           // Track # (within disc)
        public int DiscNumber { get; set; } = 1;       // Disc # (defaults to 1 for single-disc albums)
        public string SongName { get; set; }           // Just the song name (without date suffix)
        public string RawTitle { get; set; }           // Original title as stored in file (includes date/segue)
        public string TrackDate { get; set; }          // Track-specific date override (yyyy-MM-dd format, empty = inherit from album)
        public bool Segue { get; set; }                // Transitions to next track (renamed from HasSegue for consistency)
        public string Duration { get; set; }           // MM:SS format (read-only, from file)
        public bool IsModified { get; set; }           // Has user made changes?
        public bool IsMatched { get; set; }            // Song was matched in normalization (for UI highlighting)

        // Computed property: Legacy Title field for backward compatibility (maps to RawTitle if available, else SongName)
        public string Title
        {
            get => !string.IsNullOrEmpty(RawTitle) ? RawTitle : SongName;
            set => SongName = value;
        }

        // Computed property: Full display title with date and segue for library browser
        // Reconstructs from components if RawTitle doesn't have the full format
        public string DisplayTitle
        {
            get
            {
                // If RawTitle already has the date/segue format, use it
                if (!string.IsNullOrEmpty(RawTitle) &&
                    (RawTitle.Contains("(") || RawTitle.EndsWith(">")))
                {
                    return RawTitle;
                }

                // Otherwise reconstruct from components
                var title = SongName ?? "";

                // Add date if present
                if (!string.IsNullOrEmpty(TrackDate))
                {
                    title = $"{title} ({TrackDate})";
                }

                // Add segue marker if present
                if (Segue && !title.EndsWith(">"))
                {
                    title = title.TrimEnd() + " >";
                }

                return title;
            }
        }

        // Computed property: Display title with auto-appended date (what you see is what gets written)
        public string GetDisplayTitle(string albumDate)
        {
            var effectiveDate = string.IsNullOrEmpty(TrackDate) ? albumDate : TrackDate;
            var songName = SongName ?? "";

            // If effective track date differs from album date, append it
            if (!string.IsNullOrEmpty(effectiveDate) && effectiveDate != albumDate)
            {
                return $"{songName} ({effectiveDate})";
            }

            return songName;
        }

        // Computed property for segue display in library browser
        public string SegueDisplay =>
            Segue ? ">" : "";

        // Method to get final metadata title with date and segue for file writing
        public string GetFinalMetadataTitle(string albumDate)
        {
            var effectiveDate = string.IsNullOrEmpty(TrackDate) ? albumDate : TrackDate;
            var title = GetDisplayTitle(albumDate);

            // Add segue marker if present and not already in title
            if (Segue && !System.Text.RegularExpressions.Regex.IsMatch(title, @"[-–]?>|→|\[>\]\s*$"))
            {
                title = title + " >";
            }

            return title;
        }

        // Legacy property: Performance date (maps to TrackDate for backward compatibility)
        public string PerformanceDate
        {
            get => TrackDate;
            set => TrackDate = value;
        }

        // Legacy property: HasSegue (maps to Segue for backward compatibility)
        public bool HasSegue
        {
            get => Segue;
            set => Segue = value;
        }
    }
}
