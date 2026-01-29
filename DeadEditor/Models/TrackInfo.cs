namespace DeadEditor.Models
{
    public class TrackInfo
    {
        public string FilePath { get; set; }           // Full path to FLAC file
        public string FileName { get; set; }           // Just the filename
        public int TrackNumber { get; set; }           // Track # (within disc)
        public int DiscNumber { get; set; } = 1;       // Disc # (defaults to 1 for single-disc albums)
        public string Title { get; set; }              // Current title from metadata
        public string NormalizedTitle { get; set; }    // After normalization (null if not matched)
        public string PerformanceDate { get; set; }    // yyyy-MM-dd format
        public bool HasSegue { get; set; }             // Transitions to next track
        public string Duration { get; set; }           // MM:SS format (read-only, from file)
        public bool IsModified { get; set; }           // Has user made changes?
        public string PreviewMetadata { get; set; }    // Preview of final metadata to be written

        // Computed property for display in grid (shows normalized if available)
        public string DisplayTitle =>
            NormalizedTitle ?? Title;

        // Computed property for segue display in library browser
        public string Segue =>
            HasSegue ? ">" : "";

        // Method to get final metadata title with date and segue
        public string GetFinalMetadataTitle(string fallbackDate)
        {
            var date = PerformanceDate ?? fallbackDate;
            var title = NormalizedTitle ?? Title;

            // Only add segue marker if it's not already present in the title
            if (HasSegue && !System.Text.RegularExpressions.Regex.IsMatch(title, @"[-–]?>|→|\[>\]\s*$"))
            {
                title = title + " >";
            }

            // Check if title already has embedded date/venue info (bonus tracks)
            // Patterns:
            // - "Song [M/D/YY, Venue" or "[MM/DD/YYYY, Venue" (bracket format)
            // - "Song (M/D/YY Venue" or "(MM/DD/YYYY Venue" (parenthesis format)
            // - "Song (Filler: yyyy-MM-dd - Venue"
            // - "Song (yyyy-MM-dd)" or "Song (yyyy-MM-dd - Location)"
            // - "Song (1978-04-22 >" (with segue marker after the date)
            bool hasEmbeddedDateVenue = System.Text.RegularExpressions.Regex.IsMatch(
                title, @"[\[\(]\d{1,2}/\d{1,2}/\d{2,4}[\s,]") ||
                System.Text.RegularExpressions.Regex.IsMatch(
                title, @"\(Filler:\s*\d{4}-\d{2}-\d{2}", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(
                title, @"\(\d{4}-\d{2}-\d{2}");

            // Only add date suffix if title doesn't already have embedded date/venue info
            if (!hasEmbeddedDateVenue && !string.IsNullOrEmpty(date))
            {
                return $"{title} ({date})";
            }

            return title;
        }
    }
}
