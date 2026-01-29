using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    public class LibraryImportService
    {
        private readonly MetadataService _metadataService;

        public LibraryImportService(MetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        /// <summary>
        /// Imports tracks to the managed library with organized folder structure
        /// </summary>
        public void ImportToLibrary(string libraryRoot, AlbumInfo albumInfo, List<TrackInfo> tracks, IProgress<(int current, int total, string message)>? progress = null, string? officialReleasesPath = null)
        {
            if (string.IsNullOrEmpty(libraryRoot))
            {
                throw new ArgumentException("Library root path is not set");
            }

            if (!Directory.Exists(libraryRoot))
            {
                Directory.CreateDirectory(libraryRoot);
            }

            var totalTracks = tracks.Count;
            var processedTracks = 0;

            // Handle official releases
            if (albumInfo.Type == AlbumType.OfficialRelease)
            {
                if (string.IsNullOrEmpty(officialReleasesPath))
                {
                    throw new ArgumentException("Official releases path is not set");
                }

                if (!Directory.Exists(officialReleasesPath))
                {
                    Directory.CreateDirectory(officialReleasesPath);
                }

                // Official release: OfficialReleasesPath\Series\Album Name\
                // Extract series name from OfficialRelease (e.g., "Dave's Picks" from "Dave's Picks Volume 28")
                var seriesName = ExtractSeriesName(albumInfo.OfficialRelease);
                var seriesFolder = Path.Combine(officialReleasesPath, seriesName);

                var folderName = SanitizeFolderName(albumInfo.OfficialRelease);
                var targetFolder = Path.Combine(seriesFolder, folderName);

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // Import all tracks to this folder (official releases don't split by date)
                ImportTracksToFolder(targetFolder, albumInfo, tracks, ref processedTracks, totalTracks, progress, isOfficialRelease: true);
            }
            // Handle studio albums
            else if (albumInfo.Type == AlbumType.Studio)
            {
                // Studio album: LibraryRoot\Studio Albums\Album Name (Year)\
                var studioAlbumsFolder = Path.Combine(libraryRoot, "Studio Albums");

                var folderName = albumInfo.ReleaseYear.HasValue
                    ? $"{albumInfo.AlbumName} ({albumInfo.ReleaseYear.Value})"
                    : albumInfo.AlbumName;

                folderName = SanitizeFolderName(folderName);
                var targetFolder = Path.Combine(studioAlbumsFolder, folderName);

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // Import all tracks to this folder
                ImportTracksToFolder(targetFolder, albumInfo, tracks, ref processedTracks, totalTracks, progress);
            }
            else
            {
                // Live recording: Group tracks by date (for multi-show imports)
                var tracksByDate = tracks.GroupBy(t => t.PerformanceDate ?? albumInfo.Date);

                foreach (var dateGroup in tracksByDate)
                {
                    var date = dateGroup.Key;
                    var dateTracks = dateGroup.ToList();

                    // Create folder structure: LibraryRoot\Year\Date - Venue, City, State\
                    var year = DateTime.Parse(date).Year.ToString();

                    // Build folder name with proper handling of empty fields
                    var venue = string.IsNullOrWhiteSpace(albumInfo.Venue) ? "Unknown Venue" : albumInfo.Venue;
                    var city = string.IsNullOrWhiteSpace(albumInfo.City) ? "Unknown City" : albumInfo.City;
                    var state = string.IsNullOrWhiteSpace(albumInfo.State) ? "" : albumInfo.State;

                    var folderName = string.IsNullOrWhiteSpace(state)
                        ? $"{date} - {venue} - {city}"
                        : $"{date} - {venue} - {city}, {state}";

                    // Sanitize folder name (remove invalid characters)
                    folderName = SanitizeFolderName(folderName);

                    var targetFolder = Path.Combine(libraryRoot, year, folderName);

                    // Create the target folder
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    // Import tracks for this date
                    ImportTracksToFolder(targetFolder, albumInfo, dateTracks, ref processedTracks, totalTracks, progress, date);
                }
            }
        }

        /// <summary>
        /// Imports tracks to a specific folder
        /// </summary>
        private void ImportTracksToFolder(string targetFolder, AlbumInfo albumInfo, List<TrackInfo> tracks,
            ref int processedTracks, int totalTracks, IProgress<(int current, int total, string message)>? progress, string? dateForTitle = null, bool isOfficialRelease = false)
        {
            // For studio albums, dateForTitle will be null
            var isStudioAlbum = albumInfo.Type == AlbumType.Studio;

            // Copy and write metadata for each track
            foreach (var track in tracks)
            {
                processedTracks++;
                progress?.Report((processedTracks, totalTracks, $"Importing track {processedTracks} of {totalTracks}: {track.NormalizedTitle ?? track.Title}"));

                // Generate new filename
                var trackTitle = track.NormalizedTitle ?? track.Title;
                if (track.HasSegue)
                {
                    trackTitle += " >";
                }

                // Get the original file extension (e.g., .flac or .mp3)
                var extension = Path.GetExtension(track.FilePath);

                string newFileName;
                if (isStudioAlbum)
                {
                    // Studio album: "01 - Song Name.flac" or "01 - Song Name.mp3"
                    newFileName = $"{track.TrackNumber:D2} - {trackTitle}{extension}";
                }
                else if (isOfficialRelease)
                {
                    // Official release: "01 - Song Name.flac" (title already has date/venue embedded from original metadata)
                    newFileName = $"{track.TrackNumber:D2} - {trackTitle}{extension}";
                }
                else
                {
                    // Live recording: "01 - Song Name (1971-04-25).flac" or "01 - Song Name (1971-04-25).mp3"
                    var date = dateForTitle ?? track.PerformanceDate ?? albumInfo.Date;
                    newFileName = $"{track.TrackNumber:D2} - {trackTitle} ({date}){extension}";
                }

                newFileName = SanitizeFileName(newFileName);
                var targetPath = Path.Combine(targetFolder, newFileName);

                // Copy the file
                File.Copy(track.FilePath, targetPath, overwrite: true);

                // Read original metadata to preserve fields we don't explicitly set
                string? originalGenre = null;
                string? originalComment = null;
                string? originalCopyright = null;
                string? originalPublisher = null;
                string? originalComposer = null;

                try
                {
                    using (var originalFile = TagLib.File.Create(track.FilePath))
                    {
                        originalGenre = originalFile.Tag.FirstGenre;
                        originalComment = originalFile.Tag.Comment;
                        originalCopyright = originalFile.Tag.Copyright;
                        originalPublisher = originalFile.Tag.Publisher;
                        originalComposer = originalFile.Tag.FirstComposer;
                    }
                }
                catch
                {
                    // If we can't read original metadata, continue without it
                }

                // Update the track's file path temporarily for metadata writing
                var originalPath = track.FilePath;
                track.FilePath = targetPath;

                // Write metadata to the copied file
                try
                {
                    using (var file = TagLib.File.Create(targetPath))
                    {
                        var title = track.NormalizedTitle ?? track.Title;

                        if (track.HasSegue)
                        {
                            title = title + " >";
                        }

                        // Set title based on album type and track content
                        if (isStudioAlbum)
                        {
                            // Studio album track - could be studio or live bonus track
                            if (!string.IsNullOrEmpty(track.PerformanceDate))
                            {
                                // Live bonus track: Use GetFinalMetadataTitle to preserve embedded venue/date
                                file.Tag.Title = track.GetFinalMetadataTitle(track.PerformanceDate);
                            }
                            else if (!string.IsNullOrEmpty(albumInfo.Edition))
                            {
                                // Studio track with edition/remaster info
                                file.Tag.Title = $"{title} ({albumInfo.Edition})";
                            }
                            else
                            {
                                // Studio track without edition info
                                file.Tag.Title = title;
                            }
                        }
                        else if (isOfficialRelease)
                        {
                            // Official release: Use GetFinalMetadataTitle which preserves embedded date/venue info
                            file.Tag.Title = track.GetFinalMetadataTitle(albumInfo.Date);
                        }
                        else
                        {
                            // Live recording: Title with date
                            var trackDate = dateForTitle ?? track.PerformanceDate ?? albumInfo.Date;
                            file.Tag.Title = $"{title} ({trackDate})";
                        }

                        file.Tag.Album = albumInfo.AlbumTitle;
                        file.Tag.Performers = new[] { albumInfo.Artist };
                        file.Tag.AlbumArtists = new[] { albumInfo.Artist };
                        file.Tag.Track = (uint)track.TrackNumber;
                        file.Tag.Disc = (uint)track.DiscNumber;

                        // Set year based on album type
                        if (isStudioAlbum && albumInfo.ReleaseYear.HasValue)
                        {
                            file.Tag.Year = (uint)albumInfo.ReleaseYear.Value;
                        }
                        else if (!isStudioAlbum && DateTime.TryParse(albumInfo.Date, out var parsedDate))
                        {
                            file.Tag.Year = (uint)parsedDate.Year;
                        }

                        // Preserve original metadata fields that we don't explicitly set
                        if (!string.IsNullOrEmpty(originalGenre))
                        {
                            file.Tag.Genres = new[] { originalGenre };
                        }
                        if (!string.IsNullOrEmpty(originalComment))
                        {
                            file.Tag.Comment = originalComment;
                        }
                        if (!string.IsNullOrEmpty(originalCopyright))
                        {
                            file.Tag.Copyright = originalCopyright;
                        }
                        if (!string.IsNullOrEmpty(originalPublisher))
                        {
                            file.Tag.Publisher = originalPublisher;
                        }
                        if (!string.IsNullOrEmpty(originalComposer))
                        {
                            file.Tag.Composers = new[] { originalComposer };
                        }

                        // Embed artwork in this track
                        if (albumInfo.ArtworkData != null && albumInfo.ArtworkMimeType != null)
                        {
                            var picture = new TagLib.Picture
                            {
                                Type = TagLib.PictureType.FrontCover,
                                MimeType = albumInfo.ArtworkMimeType,
                                Data = albumInfo.ArtworkData,
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
                finally
                {
                    // Restore original path
                    track.FilePath = originalPath;
                }
            }
        }

        private string ExtractSeriesName(string officialRelease)
        {
            if (string.IsNullOrWhiteSpace(officialRelease))
            {
                return "Unknown Series";
            }

            // Extract series name from full release name
            // Examples:
            // "Dave's Picks Volume 28" -> "Dave's Picks"
            // "Road Trips Vol. 3 No. 4" -> "Road Trips"
            // "Dick's Picks Volume 14" -> "Dick's Picks"
            // "Download Series" -> "Download Series"
            // "Spring 1990" -> "Spring 1990"

            var patterns = new[]
            {
                @"^(Dave's Picks)",
                @"^(Dick's Picks)",
                @"^(Road Trips)",
                @"^(Download Series)",
                @"^(Spring \d{4})",
                @"^(Here Comes Sunshine)"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    officialRelease,
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // If no pattern matches, return the first few words (up to "Volume", "Vol", etc.)
            var volumeMatch = System.Text.RegularExpressions.Regex.Match(
                officialRelease,
                @"^(.+?)\s+(?:Vol(?:ume|\.)?\s+\d+|Volume\s+\d+|No\.\s+\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (volumeMatch.Success)
            {
                return volumeMatch.Groups[1].Value.Trim();
            }

            // Fallback: use the whole string
            return officialRelease;
        }

        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            // Replace invalid path characters with underscore
            var invalid = Path.GetInvalidPathChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, '_');
            }

            // Clean up multiple spaces and trim
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();

            // Remove leading/trailing periods and spaces
            name = name.Trim('.', ' ');

            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown.flac";

            // Separate extension from filename
            var extension = Path.GetExtension(name);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(name);

            // Replace invalid filename characters with underscore
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                nameWithoutExt = nameWithoutExt.Replace(c, '_');
            }

            // Clean up multiple spaces and trim
            nameWithoutExt = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"\s+", " ").Trim();

            // Remove leading/trailing periods and spaces
            nameWithoutExt = nameWithoutExt.Trim('.', ' ');

            if (string.IsNullOrWhiteSpace(nameWithoutExt))
                return "Unknown.flac";

            // Recombine with extension
            return nameWithoutExt + extension;
        }

        /// <summary>
        /// Checks if this show/album already exists in the library
        /// </summary>
        public bool ShowExistsInLibrary(string libraryRoot, AlbumInfo albumInfo, string? officialReleasesPath = null)
        {
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                return false;
            }

            if (albumInfo.Type == AlbumType.OfficialRelease)
            {
                // Check in official releases path
                if (string.IsNullOrEmpty(officialReleasesPath) || !Directory.Exists(officialReleasesPath))
                {
                    return false;
                }

                var seriesName = ExtractSeriesName(albumInfo.OfficialRelease);
                var seriesFolder = Path.Combine(officialReleasesPath, seriesName);

                if (!Directory.Exists(seriesFolder))
                {
                    return false;
                }

                var folderPattern = $"{albumInfo.OfficialRelease}*";
                var matchingFolders = Directory.GetDirectories(seriesFolder, folderPattern);
                return matchingFolders.Length > 0;
            }
            else if (albumInfo.Type == AlbumType.Studio)
            {
                // Check in Studio Albums folder
                var studioAlbumsFolder = Path.Combine(libraryRoot, "Studio Albums");

                if (!Directory.Exists(studioAlbumsFolder))
                {
                    return false;
                }

                // Build exact folder name to match (same logic as import)
                var folderName = albumInfo.ReleaseYear.HasValue
                    ? $"{albumInfo.AlbumName} ({albumInfo.ReleaseYear.Value})"
                    : albumInfo.AlbumName;

                folderName = SanitizeFolderName(folderName);
                var targetPath = Path.Combine(studioAlbumsFolder, folderName);

                return Directory.Exists(targetPath);
            }
            else
            {
                // Check in year folders for live recordings
                var year = DateTime.Parse(albumInfo.Date ?? "2000-01-01").Year.ToString();
                var folderPattern = $"{albumInfo.Date}*";
                var yearFolder = Path.Combine(libraryRoot, year);

                if (!Directory.Exists(yearFolder))
                {
                    return false;
                }

                var matchingFolders = Directory.GetDirectories(yearFolder, folderPattern);
                return matchingFolders.Length > 0;
            }
        }
    }
}
