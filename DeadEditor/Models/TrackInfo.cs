namespace DeadEditor.Models
{
    public class TrackInfo
    {
        public string FilePath { get; set; }           // Full path to FLAC file
        public string FileName { get; set; }           // Just the filename
        public int TrackNumber { get; set; }           // Track #
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

        // Method to get final metadata title with date and segue
        public string GetFinalMetadataTitle(string fallbackDate)
        {
            var date = PerformanceDate ?? fallbackDate;
            var title = NormalizedTitle ?? Title;

            if (HasSegue)
            {
                title = title + " >";
            }

            return $"{title} ({date})";
        }
    }
}
