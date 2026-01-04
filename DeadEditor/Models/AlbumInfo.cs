namespace DeadEditor.Models
{
    public class AlbumInfo
    {
        public string FolderPath { get; set; }         // Path to the folder
        public string Artist { get; set; }             // Default: "Grateful Dead"
        public string Date { get; set; }               // yyyy-MM-dd
        public string Venue { get; set; }              // e.g., "Barton Hall"
        public string City { get; set; }               // e.g., "Ithaca"
        public string State { get; set; }              // e.g., "NY"
        public string OfficialRelease { get; set; }    // e.g., "Dave's Picks Vol. 29" (optional)
        public bool IsModified { get; set; }

        // Computed property for album title
        public string AlbumTitle
        {
            get
            {
                var baseTitle = $"{Date} - {Venue} - {City}, {State}";
                if (!string.IsNullOrEmpty(OfficialRelease))
                    return $"{baseTitle} : {OfficialRelease}";
                return baseTitle;
            }
        }
    }
}
