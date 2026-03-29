namespace DeadEditor.Models
{
    /// <summary>
    /// DateRow - Display model for "By Date" view mode in the Library Grid.
    /// Each row represents a single concert date, possibly exploded from a multi-date album.
    /// </summary>
    public class DateRow
    {
        public string Date { get; set; } = "";
        public string Venue { get; set; } = "";
        public string CityState { get; set; } = "";
        public string FromAlbum { get; set; } = "";
        public int TrackCount { get; set; }

        // Back-reference to source album for double-click navigation
        public LibraryShow SourceShow { get; set; } = null!;
    }
}
