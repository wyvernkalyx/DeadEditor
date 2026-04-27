using System.Text.RegularExpressions;

namespace DeadEditor.Models
{
    public enum AlbumType
    {
        AudienceRecording,  // Audience/taper recordings (was Live)
        OfficialRelease     // Official releases (studio albums, live albums, box sets, series)
    }

    public class AlbumInfo
    {
        public string FolderPath { get; set; }         // Path to the folder
        public string Artist { get; set; }             // Artist name (always visible/editable)

        // Unified fields - all always visible and editable regardless of album type
        public string AlbumDate { get; set; }          // yyyy-MM-dd (renamed from Date for clarity)
        public string Venue { get; set; }              // Venue name (may be empty for official releases)
        public string CityState { get; set; }          // City, State combined (simplified from separate City/State)
        public string AlbumName { get; set; }          // Album or official release name
        public string CollectionName { get; set; }     // Collection/box set name (optional, applies to either type)
        public string Year { get; set; }               // Release year as string (may be empty)

        // Album type (defaults to Auto-detect)
        public AlbumType Type { get; set; } = AlbumType.AudienceRecording;

        // Folder name override
        public bool FolderNameOverride { get; set; }   // True if user manually edited folder name
        public string CustomFolderName { get; set; }   // User's custom folder name (when override=true)

        public bool IsModified { get; set; }

        // Artwork data
        public byte[]? ArtworkData { get; set; }       // Image bytes
        public string? ArtworkMimeType { get; set; }   // "image/jpeg" or "image/png"

        // Info file data
        public string? InfoFileContent { get; set; }   // Content of .txt info files found in folder
        public string? InfoFileName { get; set; }      // Name of the info file

        // Legacy properties for backward compatibility with existing code
        public string Date
        {
            get => AlbumDate;
            set => AlbumDate = value;
        }

        public string City
        {
            get
            {
                // Extract city from "City, ST" format
                if (string.IsNullOrEmpty(CityState)) return "";
                var parts = CityState.Split(new[] { ',' }, 2);
                return parts[0].Trim();
            }
            set
            {
                // Preserve state if it exists
                var state = State;
                CityState = string.IsNullOrEmpty(state) ? value : $"{value}, {state}";
            }
        }

        public string State
        {
            get
            {
                // Extract state from "City, ST" format
                if (string.IsNullOrEmpty(CityState)) return "";
                var parts = CityState.Split(new[] { ',' }, 2);
                return parts.Length > 1 ? parts[1].Trim() : "";
            }
            set
            {
                // Preserve city if it exists
                var city = City;
                CityState = string.IsNullOrEmpty(value) ? city : $"{city}, {value}";
            }
        }

        public int? ReleaseYear
        {
            get => int.TryParse(Year, out var year) ? year : null;
            set => Year = value?.ToString() ?? "";
        }

        public string OfficialRelease
        {
            get => AlbumName; // Map to unified AlbumName field
            set => AlbumName = value;
        }

        public string BoxSetName
        {
            get => AlbumName; // Map to unified AlbumName field
            set => AlbumName = value;
        }

        public string Edition { get; set; }            // Optional edition info (preserved as separate field)

        public string? MusicBrainzReleaseId { get; set; }  // MB release MBID (UUID), set by ReleaseSelectorDialog selection

        // Computed property for album title (adapts based on Type)
        public string AlbumTitle
        {
            get
            {
                // Check for user override first
                if (FolderNameOverride && !string.IsNullOrEmpty(CustomFolderName))
                {
                    return CustomFolderName;
                }

                if (Type == AlbumType.OfficialRelease)
                {
                    // Official Release with Date+Venue: "Date - Venue - City, ST - Album Name"
                    if (!string.IsNullOrEmpty(AlbumDate) && !string.IsNullOrEmpty(Venue))
                    {
                        var baseTitle = $"{AlbumDate} - {Venue} - {CityState}";
                        if (!string.IsNullOrEmpty(AlbumName))
                            baseTitle += $" - {AlbumName}";
                        if (!string.IsNullOrEmpty(CollectionName))
                            baseTitle += $" : {CollectionName}";
                        return baseTitle;
                    }
                    // Official Release without Date+Venue (studio album): "Album Name (Year)"
                    else if (!string.IsNullOrEmpty(AlbumName))
                    {
                        // Don't append Year if AlbumName already contains a parenthesized year
                        // e.g., "Europe '72 (2003 Reissue)" already has "(2003" — don't append "(1972)"
                        var hasYearInParens = Regex.IsMatch(AlbumName, @"\(\d{4}");
                        var title = (!string.IsNullOrEmpty(Year) && !hasYearInParens)
                            ? $"{AlbumName} ({Year})"
                            : AlbumName;
                        if (!string.IsNullOrEmpty(CollectionName))
                            title += $" : {CollectionName}";
                        return title;
                    }
                    return "Unknown Release";
                }
                else // AudienceRecording
                {
                    // When no concert date, fall back to album name format
                    // (e.g., non-concert folders parsed before user sets the type)
                    if (string.IsNullOrEmpty(AlbumDate) && string.IsNullOrEmpty(Venue) && !string.IsNullOrEmpty(AlbumName))
                    {
                        // Don't append Year if AlbumName already contains a parenthesized year
                        // e.g., "Europe '72 (2003 Reissue)" already has "(2003" — don't append "(1972)"
                        var hasYearInParens = Regex.IsMatch(AlbumName, @"\(\d{4}");
                        var title = (!string.IsNullOrEmpty(Year) && !hasYearInParens)
                            ? $"{AlbumName} ({Year})"
                            : AlbumName;
                        if (!string.IsNullOrEmpty(CollectionName))
                            title += $" : {CollectionName}";
                        return title;
                    }

                    // Audience recording format: "Date - Venue - City, State" [- Album Name]
                    var baseTitle = $"{AlbumDate} - {Venue} - {CityState}";
                    if (!string.IsNullOrEmpty(AlbumName))
                        baseTitle += $" - {AlbumName}";
                    if (!string.IsNullOrEmpty(CollectionName))
                        baseTitle += $" : {CollectionName}";
                    return baseTitle;
                }
            }
        }

        // Helper property to determine if this is an official release
        public bool IsOfficialRelease => Type == AlbumType.OfficialRelease;
    }
}
