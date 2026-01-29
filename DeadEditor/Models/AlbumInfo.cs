namespace DeadEditor.Models
{
    public enum AlbumType
    {
        Live,            // Live concert recording (audience/taper recording)
        Studio,          // Studio album release
        OfficialRelease, // Official live release (Dave's Picks, Road Trips, Dick's Picks, etc.)
        BoxSet           // Box set collection (digital downloads or physical box sets with multiple shows)
    }

    public class AlbumInfo
    {
        public string FolderPath { get; set; }         // Path to the folder
        public string Artist { get; set; }             // Default: "Grateful Dead"

        // Album type (defaults to Live for backward compatibility)
        public AlbumType Type { get; set; } = AlbumType.Live;

        // Live recording properties
        public string Date { get; set; }               // yyyy-MM-dd (performance date for live, null for studio)
        public string Venue { get; set; }              // e.g., "Barton Hall" (live only)
        public string City { get; set; }               // e.g., "Ithaca" (live only)
        public string State { get; set; }              // e.g., "NY" (live only)
        public string OfficialRelease { get; set; }    // e.g., "Dave's Picks Vol. 29" (optional)

        // Studio album properties
        public string AlbumName { get; set; }          // e.g., "Workingman's Dead" (studio only)
        public int? ReleaseYear { get; set; }          // e.g., 1970 (studio only)
        public string? Edition { get; set; }           // e.g., "2025 Remaster", "Deluxe Edition" (optional)

        // Box set properties
        public string? BoxSetName { get; set; }        // e.g., "Enjoying the Ride", "Digital Album Live" (box set only)

        public bool IsModified { get; set; }

        // Artwork data
        public byte[]? ArtworkData { get; set; }       // Image bytes
        public string? ArtworkMimeType { get; set; }   // "image/jpeg" or "image/png"

        // Info file data
        public string? InfoFileContent { get; set; }   // Content of .txt info files found in folder
        public string? InfoFileName { get; set; }      // Name of the info file

        // Computed property for album title (adapts based on Type)
        public string AlbumTitle
        {
            get
            {
                if (Type == AlbumType.Studio)
                {
                    // Studio album format: "Album Name (Year) [Edition]"
                    if (!string.IsNullOrEmpty(AlbumName))
                    {
                        var title = ReleaseYear.HasValue
                            ? $"{AlbumName} ({ReleaseYear.Value})"
                            : AlbumName;

                        if (!string.IsNullOrEmpty(Edition))
                            title += $" [{Edition}]";

                        return title;
                    }
                    return "Unknown Album";
                }
                else if (Type == AlbumType.BoxSet)
                {
                    // Box set format: "Date - Venue - City, State: Box Set Name"
                    var baseTitle = $"{Date} - {Venue} - {City}, {State}";
                    if (!string.IsNullOrEmpty(BoxSetName))
                        return $"{baseTitle}: {BoxSetName}";
                    return baseTitle;
                }
                else
                {
                    // Live recording format: "Date - Venue - City, State"
                    var baseTitle = $"{Date} - {Venue} - {City}, {State}";
                    if (!string.IsNullOrEmpty(OfficialRelease))
                        return $"{baseTitle} : {OfficialRelease}";
                    return baseTitle;
                }
            }
        }

        // Helper property to determine if this is a studio album
        public bool IsStudioAlbum => Type == AlbumType.Studio;
    }
}
