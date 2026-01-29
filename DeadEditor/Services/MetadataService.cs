using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    public class MetadataService
    {
        /// <summary>
        /// Reads metadata from all audio files (FLAC/MP3) in a folder
        /// </summary>
        public List<TrackInfo> ReadFolder(string folderPath)
        {
            var tracks = new List<TrackInfo>();

            // Get all supported audio files (FLAC and MP3)
            var audioFiles = Directory.GetFiles(folderPath, "*.flac")
                                     .Concat(Directory.GetFiles(folderPath, "*.mp3"))
                                     .OrderBy(f => f)
                                     .ToList();

            foreach (var filePath in audioFiles)
            {
                try
                {
                    using (var file = TagLib.File.Create(filePath))
                    {
                        var rawTitle = file.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath);

                        var track = new TrackInfo
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            TrackNumber = (int)file.Tag.Track,
                            DiscNumber = file.Tag.Disc > 0 ? (int)file.Tag.Disc : 1,
                            Title = rawTitle,
                            Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                            IsModified = false
                        };

                        // Check for existing segue markers before cleaning
                        track.HasSegue = HasSegueMarker(rawTitle);

                        // Try to extract date from existing title if in format "Song (yyyy-MM-dd)"
                        track.PerformanceDate = ExtractDateFromTitle(track.Title)
                                               ?? ExtractDateFromAlbum(file.Tag.Album);

                        // DON'T clean the title here - keep original metadata in Title field
                        // Cleaning will happen during normalization for matching purposes
                        // track.Title = CleanTitle(track.Title);  // REMOVED - this was destroying original metadata

                        tracks.Add(track);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with other files
                    System.Diagnostics.Debug.WriteLine($"Error reading {filePath}: {ex.Message}");
                }
            }

            // If no track numbers, assign based on order
            if (tracks.All(t => t.TrackNumber == 0))
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    tracks[i].TrackNumber = i + 1;
                }
            }

            var orderedTracks = tracks.OrderBy(t => t.TrackNumber).ToList();

            // Auto-detect segues based on common patterns
            DetectSegues(orderedTracks);

            return orderedTracks;
        }

        private void DetectSegues(List<TrackInfo> tracks)
        {
            // Common segue pairs in Grateful Dead shows
            var seguePairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "China Cat Sunflower", "I Know You Rider" },
                { "China Cat", "I Know You Rider" },
                { "Scarlet Begonias", "Fire on the Mountain" },
                { "Scarlet", "Fire on the Mountain" },
                { "Help on the Way", "Slipknot!" },
                { "Slipknot!", "Franklin's Tower" },
                { "Lost Sailor", "Saint of Circumstance" },
                { "Playing in the Band", "Uncle John's Band" },
                { "Estimated Prophet", "Eyes of the World" },
                { "Drums", "Space" },
                { "Space", "The Other One" }
            };

            for (int i = 0; i < tracks.Count - 1; i++)
            {
                var currentTitle = tracks[i].Title.Trim();
                var nextTitle = tracks[i + 1].Title.Trim();

                // Check if this is a known segue pair
                foreach (var pair in seguePairs)
                {
                    if (currentTitle.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) &&
                        nextTitle.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        tracks[i].HasSegue = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reads album-level metadata from first file in folder
        /// </summary>
        public AlbumInfo ReadAlbumInfo(string folderPath, List<TrackInfo> tracks)
        {
            var albumInfo = new AlbumInfo
            {
                FolderPath = folderPath,
                Artist = "Grateful Dead"
            };

            // Try to parse folder name for official release info
            // Common patterns:
            // "Artist - Date - Official Release Name"
            // "Date - Venue - City, State - Official Release"
            var folderName = Path.GetFileName(folderPath);
            ParseFolderName(folderName, albumInfo);

            var firstFile = tracks.FirstOrDefault()?.FilePath;
            if (firstFile != null)
            {
                using (var file = TagLib.File.Create(firstFile))
                {
                    albumInfo.Artist = file.Tag.FirstPerformer ?? "Grateful Dead";

                    // Try to parse album title
                    var album = file.Tag.Album;
                    if (!string.IsNullOrEmpty(album))
                    {
                        ParseAlbumTitle(album, albumInfo);
                    }

                    // Try to get date
                    if (file.Tag.Year > 0)
                    {
                        // Year only - might need to extract full date from album
                        var dateFromAlbum = ExtractDateFromAlbum(album);
                        albumInfo.Date = dateFromAlbum ?? $"{file.Tag.Year}-01-01";
                    }

                    // Read artwork from first track
                    var pictures = file.Tag.Pictures;
                    if (pictures.Length > 0)
                    {
                        albumInfo.ArtworkData = pictures[0].Data.Data;
                        albumInfo.ArtworkMimeType = pictures[0].MimeType;
                    }
                }
            }

            // Look for .txt info files in the folder
            try
            {
                var txtFiles = Directory.GetFiles(folderPath, "*.txt");
                if (txtFiles.Length > 0)
                {
                    // Take the first .txt file found
                    var infoFile = txtFiles[0];
                    albumInfo.InfoFileName = Path.GetFileName(infoFile);
                    albumInfo.InfoFileContent = File.ReadAllText(infoFile);
                }
            }
            catch
            {
                // Silently ignore errors reading info files
            }

            return albumInfo;
        }

        /// <summary>
        /// Writes metadata to all audio files (FLAC/MP3)
        /// </summary>
        public void WriteMetadata(AlbumInfo album, List<TrackInfo> tracks)
        {
            foreach (var track in tracks)
            {
                using (var file = TagLib.File.Create(track.FilePath))
                {
                    // Build the final title with date
                    var date = track.PerformanceDate ?? album.Date;
                    var title = track.NormalizedTitle ?? track.Title;

                    // Remove any existing date suffix to prevent duplicates
                    // Pattern matches: " (yyyy-MM-dd)" or " (yyyy-MM-dd) (yyyy-MM-dd)" etc.
                    title = Regex.Replace(title, @"\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$", "");

                    // Add segue marker BEFORE the date
                    if (track.HasSegue)
                    {
                        title = title + " >";
                    }

                    file.Tag.Title = $"{title} ({date})";
                    file.Tag.Album = album.AlbumTitle;
                    file.Tag.Performers = new[] { album.Artist };
                    file.Tag.AlbumArtists = new[] { album.Artist };
                    file.Tag.Track = (uint)track.TrackNumber;
                    file.Tag.Disc = (uint)track.DiscNumber;

                    // Parse year from date
                    if (DateTime.TryParse(album.Date, out var parsedDate))
                    {
                        file.Tag.Year = (uint)parsedDate.Year;
                    }

                    // Embed artwork in this track
                    if (album.ArtworkData != null && album.ArtworkMimeType != null)
                    {
                        var picture = new TagLib.Picture
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = album.ArtworkMimeType,
                            Data = album.ArtworkData,
                            Description = "Front Cover"
                        };
                        file.Tag.Pictures = new[] { picture };
                    }
                    else
                    {
                        // Clear artwork if none is set
                        file.Tag.Pictures = new TagLib.IPicture[0];
                    }

                    file.Save();
                }
            }
        }

        // Helper methods
        private bool HasSegueMarker(string title)
        {
            // Check for common segue markers: >, ->, →, [>], etc.
            return Regex.IsMatch(title, @"[-–]?>|→|\[>\]");
        }

        private string? ExtractDateFromTitle(string title)
        {
            // Match pattern: "Song (Filler: yyyy-MM-dd - Venue, City, State)"
            var match = Regex.Match(title, @"\(Filler:\s*(\d{4}-\d{2}-\d{2})\s*-", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;

            // Match pattern: "Song Name (yyyy-MM-dd)"
            match = Regex.Match(title, @"\((\d{4}-\d{2}-\d{2})\)\s*$");
            if (match.Success) return match.Groups[1].Value;

            // Match pattern: "Song (yyyy-MM-dd - Location)" (date with location)
            match = Regex.Match(title, @"\((\d{4}-\d{2}-\d{2})\s*-");
            if (match.Success) return match.Groups[1].Value;

            // Match pattern: "Song (M/D/YY Venue..." or "(MM/DD/YYYY Venue..." (bonus tracks with parentheses)
            match = Regex.Match(title, @"\((\d{1,2}/\d{1,2}/\d{2,4})\s+");
            if (match.Success)
            {
                // Convert M/D/YY or M/D/YYYY to yyyy-MM-dd
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    return date.ToString("yyyy-MM-dd");
                }
            }

            // Match pattern: "Song [M/D/YY, Venue" or "[MM/DD/YYYY, Venue" (bonus tracks with brackets)
            // Also handles: "Song [Venue M/D/YY]" format
            match = Regex.Match(title, @"\[.*?(\d{1,2}/\d{1,2}/\d{2,4}).*?\]");
            if (match.Success)
            {
                // Convert M/D/YY or M/D/YYYY to yyyy-MM-dd
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    return date.ToString("yyyy-MM-dd");
                }
            }

            // Match pattern: "Song (Live at Venue, City, State, M/D/YYYY)" or "M/DD/YYYY"
            match = Regex.Match(title, @"(\d{1,2}/\d{1,2}/\d{4})\)");
            if (match.Success)
            {
                // Convert M/D/YYYY to yyyy-MM-dd
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    return date.ToString("yyyy-MM-dd");
                }
            }

            return null;
        }

        private string? ExtractDateFromAlbum(string? album)
        {
            if (string.IsNullOrEmpty(album)) return null;

            // Match pattern: "yyyy-MM-dd - ..."
            var match = Regex.Match(album, @"^(\d{4}-\d{2}-\d{2})");
            return match.Success ? match.Groups[1].Value : null;
        }

        private string CleanTitle(string title)
        {
            // Remove leading track numbers like "01 ", "1. ", "001-", etc.
            var cleaned = Regex.Replace(title, @"^\d+[\s\.\-_]+", "");

            // Remove date suffix and segue marker - matches format: "Song (yyyy-MM-dd)"
            cleaned = Regex.Replace(cleaned, @"\s*→?\s*\(\d{4}-\d{2}-\d{2}\)\s*$", "");

            // Handle format: "Song (yyyy-MM-dd - Location)" (date with location)
            cleaned = Regex.Replace(cleaned, @"\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$", "");

            // Also handle format: "Song (Live at Venue, City, State, M/D/YYYY)" or "(Live in ...)"
            cleaned = Regex.Replace(cleaned, @"\s*\(Live (?:at|in) [^)]+\)\s*$", "", RegexOptions.IgnoreCase);

            // Handle format: "Song [Live at Venue, City, State, M/D/YYYY]" or "[Live in ...]" with square brackets
            cleaned = Regex.Replace(cleaned, @"\s*\[Live (?:at|in) [^\]]+\]\s*$", "", RegexOptions.IgnoreCase);

            // Handle segue markers like ">" or "->" or "[>]"
            cleaned = Regex.Replace(cleaned, @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$", "");

            // Clean up any trailing parentheses or brackets patterns
            cleaned = Regex.Replace(cleaned, @"\s*[\[(]Reprise[\])]\s*$", "", RegexOptions.IgnoreCase);

            return cleaned.TrimEnd('→', ' ', '-', '–', '>', '[', ']');
        }

        private void ParseFolderName(string folderName, AlbumInfo info)
        {
            if (string.IsNullOrEmpty(folderName)) return;

            // GENERIC APPROACH: Extract dates, official release, and location info from any folder pattern
            // Handles patterns like:
            // - "Artist - Date - Venue - City, State - Release"
            // - "Artist - Date1, Date2 - City, State - Venue - Release"
            // - "Date - Venue - City, State - Release"
            // - etc.

            // Step 1: Extract and remove official release name first (most distinctive pattern)
            // Handle variations like "Road Trips Vol. 3 No. 4" or "Road Trips, Vol. 3 No. 4" (with comma)
            var releaseMatch = Regex.Match(
                folderName,
                @"((?:Dave's Picks|Dick's Picks|Road Trips|Download Series|Spring \d{4}|Here Comes Sunshine)\s*,?\s*Vol(?:ume|\.)?\s+\d+(?:\s+No\.\s+\d+)?)",
                RegexOptions.IgnoreCase);

            string remainingText = folderName;
            if (releaseMatch.Success)
            {
                info.OfficialRelease = releaseMatch.Groups[1].Value.Trim();
                // Remove the release name from the string to simplify further parsing
                remainingText = folderName.Substring(0, releaseMatch.Index).Trim();
                // Remove trailing " - " or similar
                remainingText = Regex.Replace(remainingText, @"\s*-\s*$", "");
            }

            // Step 2: Extract date(s) - support single or multiple dates
            // Patterns: "1973-11-30" or "1973-11-30, 1973-12-02" or "1973-11-30 - 1973-12-02"
            var datePattern = @"(\d{4}-\d{2}-\d{2}(?:\s*[,\-]\s*\d{4}-\d{2}-\d{2})*)";
            var dateMatch = Regex.Match(remainingText, datePattern);
            if (dateMatch.Success)
            {
                var dateString = dateMatch.Groups[1].Value.Trim();
                // If multiple dates, take the first one for album-level date
                var dates = Regex.Matches(dateString, @"\d{4}-\d{2}-\d{2}");
                if (dates.Count > 0)
                {
                    info.Date = dates[0].Value;
                }
                // Remove the date(s) from remaining text
                remainingText = remainingText.Replace(dateMatch.Groups[1].Value, "").Trim();
            }

            // Step 3: Remove artist name from the beginning if present
            remainingText = Regex.Replace(remainingText, @"^Grateful Dead\s*-?\s*", "", RegexOptions.IgnoreCase).Trim();
            remainingText = Regex.Replace(remainingText, @"^New Riders of the Purple Sage\s*-?\s*", "", RegexOptions.IgnoreCase).Trim();
            remainingText = Regex.Replace(remainingText, @"^[^-]+-\s*", "").Trim(); // Remove any other artist name

            // Clean up any leading/trailing dashes
            remainingText = remainingText.Trim('-', ' ');

            // Step 4: Parse venue/city/state from remaining text
            // Now remainingText should contain location info in some form
            // Common patterns: "Venue - City, State", "City, State - Venue", "Boston Music Hall - Boston, MA"

            if (!string.IsNullOrEmpty(remainingText))
            {
                // Pattern A: "Venue - City, State" (most common for official releases)
                var patternA = Regex.Match(remainingText, @"^(.+?)\s*-\s*([^,]+),\s*(.+)$");
                if (patternA.Success)
                {
                    info.Venue = patternA.Groups[1].Value.Trim();
                    info.City = patternA.Groups[2].Value.Trim();
                    info.State = patternA.Groups[3].Value.Trim();
                }
                // Pattern B: "City, State - Venue"
                else
                {
                    var patternB = Regex.Match(remainingText, @"^([^,]+),\s*([^-]+?)\s*-\s*(.+)$");
                    if (patternB.Success)
                    {
                        info.City = patternB.Groups[1].Value.Trim();
                        info.State = patternB.Groups[2].Value.Trim();
                        info.Venue = patternB.Groups[3].Value.Trim();
                    }
                    // Pattern C: Just "City, State" or just "Venue"
                    else
                    {
                        var parts = remainingText.Split(new[] { " - ", ", " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 1)
                        {
                            info.Venue = parts[0].Trim();
                        }
                        else if (parts.Length == 2)
                        {
                            // Could be "City, State" or "Venue - City"
                            info.City = parts[0].Trim();
                            info.State = parts[1].Trim();
                        }
                        else if (parts.Length >= 3)
                        {
                            // "Venue, City, State" or "City, State, Extra"
                            info.Venue = parts[0].Trim();
                            info.City = parts[1].Trim();
                            info.State = parts[2].Trim();
                        }
                    }
                }
            }
        }

        private void ParseAlbumTitle(string album, AlbumInfo info)
        {
            // Pattern 1: Check for Box Set format first: ": Box Set Name" (NO space before colon)
            // Box Set: "1972-09-15 - Boston Music Hall - Boston, MA: Enjoying the Ride"
            // Use negative lookbehind (?<!\s) to ensure NO space before the colon
            var boxSetMatch = Regex.Match(
                album,
                @"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:\s]+)(?<!\s):\s*(.+)$");

            if (boxSetMatch.Success)
            {
                // Box Set format (no space before colon)
                info.Date = boxSetMatch.Groups[1].Value;
                info.Venue = boxSetMatch.Groups[2].Value.Trim();
                info.City = boxSetMatch.Groups[3].Value.Trim();
                info.State = boxSetMatch.Groups[4].Value.Trim();
                info.BoxSetName = boxSetMatch.Groups[5].Value.Trim();
                info.Type = AlbumType.BoxSet;
                return;
            }

            // Pattern 2: Check for Official Release format: " : Release Name" (space before colon)
            // Official Release: "1972-09-15 - Boston Music Hall - Boston, MA : Dave's Picks Vol. 1"
            var officialMatch = Regex.Match(
                album,
                @"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:]+?)\s:\s*(.+)$");

            if (officialMatch.Success)
            {
                // Official Release format (space before colon)
                info.Date = officialMatch.Groups[1].Value;
                info.Venue = officialMatch.Groups[2].Value.Trim();
                info.City = officialMatch.Groups[3].Value.Trim();
                info.State = officialMatch.Groups[4].Value.Trim();
                info.OfficialRelease = officialMatch.Groups[5].Value.Trim();
                info.Type = AlbumType.Live;
                return;
            }

            // Pattern 3: Basic live recording format (no colon)
            // Live: "1972-09-15 - Boston Music Hall - Boston, MA"
            var liveMatch = Regex.Match(
                album,
                @"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*(.+)$");

            if (liveMatch.Success)
            {
                info.Date = liveMatch.Groups[1].Value;
                info.Venue = liveMatch.Groups[2].Value.Trim();
                info.City = liveMatch.Groups[3].Value.Trim();
                info.State = liveMatch.Groups[4].Value.Trim();
                info.Type = AlbumType.Live;
                return;
            }

            // Pattern 4: "Venue, City, State (M/D/YY & M/D/YY) [Live]" or similar
            var altMatch = Regex.Match(album, @"^([^,]+),\s*([^,]+),\s*([A-Z]{2})\s*\(([^)]+)\)");
            if (altMatch.Success)
            {
                info.Venue = altMatch.Groups[1].Value.Trim();
                info.City = altMatch.Groups[2].Value.Trim();
                info.State = altMatch.Groups[3].Value.Trim();

                // Extract first date from the date range
                var dateStr = altMatch.Groups[4].Value;
                var dateMatch = Regex.Match(dateStr, @"(\d{1,2}/\d{1,2}/\d{2,4})");
                if (dateMatch.Success)
                {
                    if (DateTime.TryParse(dateMatch.Groups[1].Value, out var date))
                    {
                        info.Date = date.ToString("yyyy-MM-dd");
                    }
                }

                // Check for official release in brackets (but ignore generic [Live] tag)
                var releaseMatch = Regex.Match(album, @"\[([^\]]+)\]");
                if (releaseMatch.Success && releaseMatch.Groups[1].Value != "Live")
                {
                    info.OfficialRelease = releaseMatch.Groups[1].Value.Trim();
                }
            }
        }
    }
}
