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
        /// Reads metadata from all FLAC files in a folder
        /// </summary>
        public List<TrackInfo> ReadFolder(string folderPath)
        {
            var tracks = new List<TrackInfo>();
            var flacFiles = Directory.GetFiles(folderPath, "*.flac")
                                     .OrderBy(f => f)
                                     .ToList();

            foreach (var filePath in flacFiles)
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
                            Title = rawTitle,
                            Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                            IsModified = false
                        };

                        // Check for existing segue markers before cleaning
                        track.HasSegue = HasSegueMarker(rawTitle);

                        // Try to extract date from existing title if in format "Song (yyyy-MM-dd)"
                        track.PerformanceDate = ExtractDateFromTitle(track.Title)
                                               ?? ExtractDateFromAlbum(file.Tag.Album);

                        // Clean the title (remove date suffix and segue markers)
                        track.Title = CleanTitle(track.Title);

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
        /// Writes metadata to all FLAC files
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
            // Match pattern: "Song Name (yyyy-MM-dd)"
            var match = Regex.Match(title, @"\((\d{4}-\d{2}-\d{2})\)\s*$");
            if (match.Success) return match.Groups[1].Value;

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

            // Also handle format: "Song (Live at Venue, City, State, M/D/YYYY)"
            cleaned = Regex.Replace(cleaned, @"\s*\(Live at [^)]+\)\s*$", "", RegexOptions.IgnoreCase);

            // Handle format: "Song [Live at Venue, City, State, M/D/YYYY]" with square brackets
            cleaned = Regex.Replace(cleaned, @"\s*\[Live at [^\]]+\]\s*$", "", RegexOptions.IgnoreCase);

            // Handle segue markers like ">" or "->" or "[>]"
            cleaned = Regex.Replace(cleaned, @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$", "");

            // Clean up any trailing parentheses or brackets patterns
            cleaned = Regex.Replace(cleaned, @"\s*[\[(]Reprise[\])]\s*$", "", RegexOptions.IgnoreCase);

            return cleaned.TrimEnd('→', ' ', '-', '–', '>', '[', ']');
        }

        private void ParseAlbumTitle(string album, AlbumInfo info)
        {
            // Pattern 1: "yyyy-MM-dd - Venue - City, State" or with " : Release"
            var match = Regex.Match(
                album,
                @"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([A-Z]{2})(?:\s*:\s*(.+))?$");

            if (match.Success)
            {
                info.Date = match.Groups[1].Value;
                info.Venue = match.Groups[2].Value.Trim();
                info.City = match.Groups[3].Value.Trim();
                info.State = match.Groups[4].Value.Trim();
                if (match.Groups[5].Success)
                {
                    info.OfficialRelease = match.Groups[5].Value.Trim();
                }
                return;
            }

            // Pattern 2: "Venue, City, State (M/D/YY & M/D/YY) [Live]" or similar
            match = Regex.Match(album, @"^([^,]+),\s*([^,]+),\s*([A-Z]{2})\s*\(([^)]+)\)");
            if (match.Success)
            {
                info.Venue = match.Groups[1].Value.Trim();
                info.City = match.Groups[2].Value.Trim();
                info.State = match.Groups[3].Value.Trim();

                // Extract first date from the date range
                var dateStr = match.Groups[4].Value;
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
