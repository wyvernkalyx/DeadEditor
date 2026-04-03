using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

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

            // Handle official releases (studio albums, live albums, box sets, series)
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

                string targetFolder;

                // Determine folder structure based on whether it's a series release or standalone album
                if (!string.IsNullOrEmpty(albumInfo.OfficialRelease))
                {
                    // Series release (Dave's Picks, Dick's Picks, etc.)
                    var seriesName = SanitizeFolderName(ExtractSeriesName(albumInfo.OfficialRelease));
                    var folderName = SanitizeFolderName(albumInfo.OfficialRelease);
                    targetFolder = Path.Combine(officialReleasesPath, seriesName, folderName);
                }
                else
                {
                    // Studio album or standalone official release
                    var folderName = !string.IsNullOrEmpty(albumInfo.AlbumName)
                        ? (albumInfo.ReleaseYear.HasValue
                            ? $"{albumInfo.AlbumName} ({albumInfo.ReleaseYear.Value})"
                            : albumInfo.AlbumName)
                        : "Unknown Album";

                    folderName = SanitizeFolderName(folderName);
                    targetFolder = Path.Combine(officialReleasesPath, "Studio Albums", folderName);
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // Import all tracks to this folder (official releases don't split by date)
                ImportTracksToFolder(targetFolder, albumInfo, tracks, ref processedTracks, totalTracks, progress, isOfficialRelease: true);
            }
            else // AudienceRecording
            {
                // Audience recording: Group tracks by date (for multi-show imports)
                var tracksByDate = tracks.GroupBy(t =>
                    string.IsNullOrEmpty(t.PerformanceDate) ? (albumInfo.Date ?? "") : t.PerformanceDate);

                foreach (var dateGroup in tracksByDate)
                {
                    var date = dateGroup.Key;
                    var dateTracks = dateGroup.ToList();

                    // Skip empty dates (shouldn't happen for audience recordings, but be safe)
                    if (string.IsNullOrWhiteSpace(date))
                    {
                        throw new InvalidOperationException("Audience recordings must have a performance date");
                    }

                    // Create folder structure: LibraryRoot\Year\Date - Venue, City, State\
                    if (!DateTime.TryParse(date, out var parsedDate))
                    {
                        throw new InvalidOperationException($"Invalid date format: {date}. Expected yyyy-MM-dd format.");
                    }

                    var year = parsedDate.Year.ToString();

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
            var isStudioAlbum = albumInfo.Type == AlbumType.OfficialRelease;

            // Copy and write metadata for each track
            foreach (var track in tracks)
            {
                processedTracks++;
                progress?.Report((processedTracks, totalTracks, $"Importing track {processedTracks} of {totalTracks}: {track.SongName ?? track.Title}"));

                // Generate new filename
                var trackTitle = track.SongName ?? track.Title;
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

                Debug.WriteLine($"[IMPORT] Copying: {track.FilePath} → {targetPath}");

                // Copy the file with retry logic for transient IOExceptions
                // (e.g., antivirus scanning, delayed file handle release from TagLib)
                CopyFileWithRetry(track.FilePath, targetPath);

                // Read original metadata to preserve fields we don't explicitly set
                string? originalGenre = null;
                string? originalComment = null;
                string? originalCopyright = null;
                string? originalPublisher = null;
                string? originalComposer = null;

                try
                {
                    Debug.WriteLine($"[IMPORT] Reading original metadata: {track.FilePath}");
                    using (var originalFile = TagLib.File.Create(track.FilePath))
                    {
                        originalGenre = originalFile.Tag.FirstGenre;
                        originalComment = originalFile.Tag.Comment;
                        originalCopyright = originalFile.Tag.Copyright;
                        originalPublisher = originalFile.Tag.Publisher;
                        originalComposer = originalFile.Tag.FirstComposer;
                    }
                }
                catch (Exception ex)
                {
                    // If we can't read original metadata, continue without it
                    Debug.WriteLine($"[IMPORT] Warning: Could not read original metadata from {track.FilePath}: {ex.Message}");
                }

                // Update the track's file path temporarily for metadata writing
                var originalPath = track.FilePath;
                track.FilePath = targetPath;

                // Write metadata to the copied file
                Debug.WriteLine($"[IMPORT] Writing metadata to: {targetPath}");
                try
                {
                    using (var file = TagLib.File.Create(targetPath))
                    {
                        var songName = track.SongName ?? track.Title;

                        // Build final title with date suffix using centralized logic
                        // trackDate resolution: explicit dateForTitle param → track.TrackDate → albumInfo.Date
                        var effectiveTrackDate = dateForTitle ?? track.TrackDate;
                        file.Tag.Title = BuildFinalTitle(songName, track.HasSegue, effectiveTrackDate, albumInfo.Date);

                        // For Official Releases, ALBUM tag = release name only.
                        // For Audience Recordings, ALBUM tag = "Date - Venue - City, ST".
                        if (albumInfo.Type == AlbumType.OfficialRelease && !string.IsNullOrEmpty(albumInfo.AlbumName))
                        {
                            file.Tag.Album = albumInfo.AlbumName;
                        }
                        else
                        {
                            file.Tag.Album = albumInfo.AlbumTitle;
                        }
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

                        // Write custom metadata fields (ALBUMDATE, VENUE, etc.)
                        // These are used by LibraryGridView to populate show data without re-parsing folder names
                        if (file is TagLib.Flac.File flacFile)
                        {
                            var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                            if (xiph != null)
                            {
                                xiph.SetField("ALBUMDATE", albumInfo.AlbumDate ?? "");
                                xiph.SetField("VENUE", albumInfo.Venue ?? "");
                                xiph.SetField("CITYSTATE", albumInfo.CityState ?? "");
                                xiph.SetField("ALBUMNAME", albumInfo.AlbumName ?? "");
                                xiph.SetField("ALBUMTYPE", albumInfo.Type.ToString());
                            }
                        }
                        else
                        {
                            var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2, true);
                            if (id3v2 != null)
                            {
                                SetId3v2TextField(id3v2, "ALBUMDATE", albumInfo.AlbumDate ?? "");
                                SetId3v2TextField(id3v2, "VENUE", albumInfo.Venue ?? "");
                                SetId3v2TextField(id3v2, "CITYSTATE", albumInfo.CityState ?? "");
                                SetId3v2TextField(id3v2, "ALBUMNAME", albumInfo.AlbumName ?? "");
                                SetId3v2TextField(id3v2, "ALBUMTYPE", albumInfo.Type.ToString());
                            }
                        }

                        file.Save();
                        Debug.WriteLine($"[IMPORT] Successfully wrote: {targetPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IMPORT ERROR] Failed writing metadata to {targetPath}: {ex.GetType().Name}: {ex.Message}");
                    throw; // Re-throw to propagate to caller
                }
                finally
                {
                    // Restore original path
                    track.FilePath = originalPath;
                }
            }
        }

        /// <summary>
        /// Writes a user-defined TXXX text frame to an ID3v2 tag (for MP3 custom fields).
        /// </summary>
        private static void SetId3v2TextField(TagLib.Id3v2.Tag tag, string description, string value)
        {
            var existing = TagLib.Id3v2.UserTextInformationFrame.Get(tag, description, false);
            if (existing != null)
                tag.RemoveFrame(existing);

            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(tag, description, true);
            frame.Text = new[] { value ?? "" };
        }

        /// <summary>
        /// Copies a file with retry logic for transient IOExceptions.
        /// Retries up to 3 times with 500ms delay between attempts.
        /// Common causes: antivirus scanning, delayed file handle release, Windows indexer.
        /// </summary>
        private static void CopyFileWithRetry(string source, string destination, int maxRetries = 3)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    File.Copy(source, destination, overwrite: true);
                    return;
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    Debug.WriteLine($"[IMPORT] Retry {attempt + 1}/{maxRetries} for copy: {ex.Message}");
                    Thread.Sleep(500);
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

            // Replace colon with " -" first for readability
            // e.g., "Listen to the River: St. Louis" → "Listen to the River - St. Louis"
            name = name.Replace(": ", " - ").Replace(":", "-");

            // Replace remaining invalid filename characters with underscore
            // GetInvalidFileNameChars includes \ / * ? " < > | (and control chars)
            // GetInvalidPathChars does NOT include : * ? " < > | so it's insufficient
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, '_');
            }

            // Clean up multiple spaces and trim
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();

            // Remove leading/trailing periods and spaces (Windows doesn't allow them)
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

                var seriesName = SanitizeFolderName(ExtractSeriesName(albumInfo.OfficialRelease));
                var seriesFolder = Path.Combine(officialReleasesPath, seriesName);

                if (!Directory.Exists(seriesFolder))
                {
                    return false;
                }

                var folderPattern = $"{SanitizeFolderName(albumInfo.OfficialRelease)}*";
                var matchingFolders = Directory.GetDirectories(seriesFolder, folderPattern);
                return matchingFolders.Length > 0;
            }
            else if (albumInfo.Type == AlbumType.OfficialRelease)
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
                // Check in year folders for audience recordings
                if (string.IsNullOrWhiteSpace(albumInfo.Date))
                {
                    return false; // Can't find audience recording without a date
                }

                if (!DateTime.TryParse(albumInfo.Date, out var parsedDate))
                {
                    return false; // Invalid date format
                }

                var year = parsedDate.Year.ToString();
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

        /// <summary>
        /// Builds the final title string to write to TITLE tag with date suffix and double-date prevention.
        /// </summary>
        /// <param name="songName">Song name (without date/segue)</param>
        /// <param name="hasSegue">Whether song has segue marker</param>
        /// <param name="trackDate">Track-specific date (TrackInfo.TrackDate)</param>
        /// <param name="albumDate">Album-level date fallback (AlbumInfo.AlbumDate)</param>
        /// <returns>Final title string: "Song > (yyyy-MM-dd)" or "Song (yyyy-MM-dd)" or "Song" if no date</returns>
        private string BuildFinalTitle(string songName, bool hasSegue, string? trackDate, string? albumDate)
        {
            // Resolve the final date: trackDate wins, albumDate is fallback
            var finalDate = !string.IsNullOrEmpty(trackDate) ? trackDate : albumDate;

            // Start with song name
            var title = songName ?? "";

            // DOUBLE-DATE PREVENTION: Check if songName already has a date suffix
            var datePattern = @"\s*\((\d{4}-\d{2}-\d{2})\)\s*$";
            var match = System.Text.RegularExpressions.Regex.Match(title, datePattern);

            if (match.Success)
            {
                var embeddedDate = match.Groups[1].Value;

                // If embedded date matches finalDate, use title as-is (don't append again)
                if (embeddedDate == finalDate)
                {
                    // Add segue marker if needed (before the existing date)
                    if (hasSegue && !title.Contains(">"))
                    {
                        title = System.Text.RegularExpressions.Regex.Replace(title, datePattern, $" > ({embeddedDate})");
                    }
                    return title;
                }
                else
                {
                    // Embedded date DIFFERS from trackDate/albumDate
                    // This is a data integrity issue - log warning and use trackDate (explicit value wins)
                    System.Diagnostics.Debug.WriteLine($"WARNING: Song '{songName}' has embedded date '{embeddedDate}' but trackDate/albumDate is '{finalDate}'. Using trackDate/albumDate.");

                    // Strip embedded date and proceed to append the correct one
                    title = System.Text.RegularExpressions.Regex.Replace(title, datePattern, "");
                }
            }

            // Add segue marker BEFORE date (must come before date suffix)
            if (hasSegue && !title.EndsWith(">"))
            {
                title = title.TrimEnd() + " >";
            }

            // Add date suffix (only if we have a valid date)
            if (!string.IsNullOrEmpty(finalDate))
            {
                title = $"{title} ({finalDate})";
            }

            return title;
        }
    }
}
