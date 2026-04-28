using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DeadEditor.Services
{
    /// <summary>
    /// Result of a per-file conflict prompt during import.
    /// </summary>
    public enum ConflictAction
    {
        Overwrite,
        Skip,
        Rename,
        CancelImport
    }

    /// <summary>
    /// Result from a conflict prompt, including the chosen action and whether to apply to all remaining.
    /// </summary>
    public class ConflictResult
    {
        public ConflictAction Action { get; set; }
        public bool ApplyToAll { get; set; }
    }

    /// <summary>
    /// Callback delegate for per-file conflict prompts during import.
    /// </summary>
    /// <param name="targetFileName">The filename that already exists in the managed folder.</param>
    /// <param name="sourceFilePath">Full path to the source file being copied.</param>
    /// <param name="context">Description of the conflict context (e.g., "re-import" or "flatten collision").</param>
    /// <returns>The user's chosen action and whether to apply to all remaining conflicts.</returns>
    public delegate ConflictResult ConflictPromptCallback(string targetFileName, string sourceFilePath, string context);

    public class LibraryImportService
    {
        private readonly MetadataService _metadataService;
        private readonly MusicBrainzService _musicBrainzService;

        public LibraryImportService(MetadataService metadataService, MusicBrainzService musicBrainzService)
        {
            _metadataService = metadataService;
            _musicBrainzService = musicBrainzService;
        }

        /// <summary>
        /// Imports tracks to the managed library with organized folder structure.
        /// All album types use the universal folder convention:
        ///   {LibraryRoot}/{Artist}/{BuildLibraryFolderName()}/
        /// </summary>
        public void ImportToLibrary(string libraryRoot, AlbumInfo albumInfo, List<TrackInfo> tracks,
            IProgress<(int current, int total, string message)>? progress = null,
            string? sourceFolderPath = null,
            ConflictPromptCallback? onConflict = null)
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

            // Universal folder structure: {LibraryRoot}/{Artist}/{AlbumFolder}/
            var artistFolder = SanitizeFolderName(albumInfo.Artist ?? "Unknown Artist");
            var albumFolder = BuildLibraryFolderName(albumInfo);
            var targetFolder = Path.Combine(libraryRoot, artistFolder, albumFolder);

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // Pre-compute fingerprints into TrackInfo.AcoustIdFingerprint before the
            // per-track copy/write loop. Sequential — fpcalc invocations block via
            // .GetAwaiter().GetResult(). Already on a background thread (Task.Run from ImportView).
            int fingerprintFailures = PrecomputeFingerprints(tracks, progress);

            ImportTracksToFolder(targetFolder, albumInfo, tracks, ref processedTracks, totalTracks, progress);

            if (fingerprintFailures > 0)
            {
                progress?.Report((totalTracks, totalTracks,
                    $"({fingerprintFailures} of {tracks.Count} tracks not fingerprinted — fpcalc not configured or unavailable)"));
            }

            // Phase 2: Copy non-audio files from source folder (flattened)
            if (!string.IsNullOrEmpty(sourceFolderPath) && Directory.Exists(sourceFolderPath))
            {
                CopyNonAudioFiles(sourceFolderPath, targetFolder, progress, onConflict);
            }
        }

        /// <summary>
        /// Delegates to the shared <see cref="FingerprintService"/> to compute Chromaprint
        /// fingerprints for any tracks that don't already carry one. Returns the number of
        /// tracks that ended up without a fingerprint (e.g., fpcalc unavailable or per-track
        /// failure) so the existing import-status reporting contract is preserved.
        /// </summary>
        /// <remarks>
        /// The import path does not pass an onTrackComplete callback — fingerprint tags are
        /// persisted later by <see cref="WriteMetadataWithRetry"/>'s full-tag write pass.
        /// </remarks>
        private int PrecomputeFingerprints(List<TrackInfo> tracks,
            IProgress<(int current, int total, string message)>? progress)
        {
            var fingerprintService = new FingerprintService(_musicBrainzService);
            var result = fingerprintService
                .PrecomputeFingerprintsAsync(tracks, progress)
                .GetAwaiter()
                .GetResult();
            return result.Failed;
        }

        /// <summary>
        /// Imports tracks to a specific folder
        /// </summary>
        private void ImportTracksToFolder(string targetFolder, AlbumInfo albumInfo, List<TrackInfo> tracks,
            ref int processedTracks, int totalTracks, IProgress<(int current, int total, string message)>? progress, string? dateForTitle = null)
        {
            // OfficialRelease type covers both studio albums and live official releases (Dave's Picks, etc.)
            // Both use filenames without date; only AudienceRecording gets date in filename
            var isOfficialRelease = albumInfo.Type == AlbumType.OfficialRelease;

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
                if (isOfficialRelease)
                {
                    // Official release (studio or live): "01 - Song Name.flac" — no date in filename
                    newFileName = $"{track.TrackNumber:D2} - {trackTitle}{extension}";
                }
                else
                {
                    // Audience recording: "01 - Song Name (1971-04-25).flac"
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
                    // PictureLazy: skip loading embedded artwork (only need Genre, Comment, etc.)
                    using (var originalFile = TagLib.File.Create(track.FilePath, TagLib.ReadStyle.PictureLazy))
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

                // Write metadata to the copied file with retry logic.
                // Newly-copied files on Windows can be transiently locked by Defender,
                // Search Indexer, or NTFS journal flushing. Retry the entire open-modify-save
                // sequence so a fresh file handle is acquired on each attempt.
                Debug.WriteLine($"[IMPORT] Writing metadata to: {targetPath}");
                try
                {
                    WriteMetadataWithRetry(targetPath, track, albumInfo, isOfficialRelease, dateForTitle,
                        originalGenre, originalComment, originalCopyright, originalPublisher, originalComposer);
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

        /// <summary>
        /// Opens a copied file, writes all metadata tags, and saves with retry logic.
        /// Retries the entire open-modify-save sequence up to 3 times with 500ms delay.
        /// This handles transient file locks from antivirus, Windows indexer, or delayed
        /// handle release after File.Copy.
        /// </summary>
        private void WriteMetadataWithRetry(string targetPath, TrackInfo track, AlbumInfo albumInfo,
            bool isOfficialRelease, string? dateForTitle,
            string? originalGenre, string? originalComment, string? originalCopyright,
            string? originalPublisher, string? originalComposer, int maxRetries = 3)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
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
                        if (isOfficialRelease && albumInfo.ReleaseYear.HasValue)
                        {
                            file.Tag.Year = (uint)albumInfo.ReleaseYear.Value;
                        }
                        else if (!isOfficialRelease && DateTime.TryParse(albumInfo.Date, out var parsedDate))
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
                                if (!string.IsNullOrEmpty(albumInfo.MusicBrainzReleaseId))
                                    xiph.SetField("MUSICBRAINZ_ALBUMID", albumInfo.MusicBrainzReleaseId);
                                if (!string.IsNullOrEmpty(track.AcoustIdFingerprint))
                                    xiph.SetField("ACOUSTID_FINGERPRINT", track.AcoustIdFingerprint);
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
                                if (!string.IsNullOrEmpty(albumInfo.MusicBrainzReleaseId))
                                    SetId3v2TextField(id3v2, "MusicBrainz Album Id", albumInfo.MusicBrainzReleaseId);
                                if (!string.IsNullOrEmpty(track.AcoustIdFingerprint))
                                    SetId3v2TextField(id3v2, "Acoustid Fingerprint", track.AcoustIdFingerprint);
                            }
                        }

                        file.Save();
                        Debug.WriteLine($"[IMPORT] Successfully wrote: {targetPath}");
                    }
                    return; // Success — exit retry loop
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    Debug.WriteLine($"[IMPORT] Retry {attempt + 1}/{maxRetries} for metadata write to {targetPath}: {ex.Message}");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IMPORT ERROR] Failed writing metadata to {targetPath}: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Builds the universal library folder name from AlbumInfo fields.
        /// Live recordings: "{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}"
        /// Studio albums:   "{Artist} - {Year} - {AlbumName}"
        /// Segments with empty values are omitted (except Artist which falls back to "Unknown Artist").
        /// </summary>
        public string BuildLibraryFolderName(AlbumInfo albumInfo)
        {
            var segments = new List<string>();

            // Artist is always present
            segments.Add(albumInfo.Artist ?? "Unknown Artist");

            bool isStudioAlbum = albumInfo.Type == AlbumType.OfficialRelease
                && (string.IsNullOrEmpty(albumInfo.AlbumDate) || string.IsNullOrEmpty(albumInfo.Venue));

            if (isStudioAlbum)
            {
                // Studio album: {Artist} - {Year} - {AlbumName}
                if (!string.IsNullOrEmpty(albumInfo.Year))
                    segments.Add(albumInfo.Year);

                if (!string.IsNullOrEmpty(albumInfo.AlbumName))
                    segments.Add(albumInfo.AlbumName);
            }
            else
            {
                // Live recording (audience or official release with date+venue):
                // {Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}
                if (!string.IsNullOrEmpty(albumInfo.AlbumDate))
                    segments.Add(albumInfo.AlbumDate);

                if (!string.IsNullOrEmpty(albumInfo.Venue))
                    segments.Add(albumInfo.Venue);

                if (!string.IsNullOrEmpty(albumInfo.CityState))
                    segments.Add(albumInfo.CityState);

                if (!string.IsNullOrEmpty(albumInfo.AlbumName))
                    segments.Add(albumInfo.AlbumName);
            }

            var folderName = string.Join(" - ", segments);
            return SanitizeFolderName(folderName);
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

        // ===== SKIPLIST =====

        private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Thumbs.db",
            ".DS_Store",
            "desktop.ini"
        };

        private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".lnk"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".flac",
            ".mp3"
        };

        private static bool ShouldSkipFile(string fileName)
        {
            if (SkipFileNames.Contains(fileName))
                return true;

            var ext = Path.GetExtension(fileName);
            if (SkipExtensions.Contains(ext))
                return true;

            return false;
        }

        /// <summary>
        /// Copies all non-audio files from the source folder (recursively) into the managed folder (flattened).
        /// Skips audio files (already copied in Phase 1) and OS junk files.
        /// </summary>
        private void CopyNonAudioFiles(string sourceFolderPath, string targetFolder,
            IProgress<(int current, int total, string message)>? progress,
            ConflictPromptCallback? onConflict)
        {
            // Enumerate all files recursively
            var allFiles = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);

            // Filter: skip audio files and skiplist files
            var filesToCopy = new List<string>();
            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file);
                var ext = Path.GetExtension(file);

                // Skip audio files (already handled in Phase 1)
                if (AudioExtensions.Contains(ext))
                    continue;

                // Skip OS junk files
                if (ShouldSkipFile(fileName))
                    continue;

                filesToCopy.Add(file);
            }

            if (filesToCopy.Count == 0)
                return;

            // Track "Apply to all" state within this import session
            ConflictAction? applyToAllAction = null;
            var copiedCount = 0;
            var failureCount = 0;
            var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < filesToCopy.Count; i++)
            {
                var sourceFile = filesToCopy[i];
                var fileName = Path.GetFileName(sourceFile);
                var targetPath = Path.Combine(targetFolder, fileName);

                progress?.Report((i + 1, filesToCopy.Count, $"Copying additional files... {i + 1} of {filesToCopy.Count}: {fileName}"));

                try
                {
                    // Check for collision: either with existing file in managed folder, or with a file already copied in this batch
                    bool collision = File.Exists(targetPath) || copiedNames.Contains(fileName);

                    if (collision)
                    {
                        if (applyToAllAction.HasValue)
                        {
                            // Use remembered action
                            targetPath = HandleConflictAction(applyToAllAction.Value, sourceFile, targetPath, targetFolder);
                            if (targetPath == null)
                                continue; // Skip
                        }
                        else if (onConflict != null)
                        {
                            var context = copiedNames.Contains(fileName)
                                ? "Another file from this import already copied"
                                : "File already exists in managed folder";

                            var result = onConflict(fileName, sourceFile, context);

                            if (result.Action == ConflictAction.CancelImport)
                            {
                                Debug.WriteLine($"[IMPORT] Import cancelled by user during non-audio copy");
                                progress?.Report((filesToCopy.Count, filesToCopy.Count,
                                    $"Import cancelled. Copied {copiedCount} additional files before cancellation."));
                                return;
                            }

                            if (result.ApplyToAll)
                                applyToAllAction = result.Action;

                            targetPath = HandleConflictAction(result.Action, sourceFile, targetPath, targetFolder);
                            if (targetPath == null)
                                continue; // Skip
                        }
                        else
                        {
                            // No conflict callback — fall back to overwrite (legacy behavior)
                            Debug.WriteLine($"[IMPORT] Overwriting (no conflict callback): {fileName}");
                        }
                    }

                    CopyFileWithRetry(sourceFile, targetPath);
                    copiedNames.Add(Path.GetFileName(targetPath));
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Debug.WriteLine($"[IMPORT] Warning: Failed to copy non-audio file {sourceFile}: {ex.Message}");
                }
            }

            var message = $"Copied {copiedCount} additional file{(copiedCount == 1 ? "" : "s")}";
            if (failureCount > 0)
                message += $" ({failureCount} failure{(failureCount == 1 ? "" : "s")})";
            progress?.Report((filesToCopy.Count, filesToCopy.Count, message));
        }

        /// <summary>
        /// Applies a conflict action and returns the final target path, or null if the file should be skipped.
        /// </summary>
        private static string? HandleConflictAction(ConflictAction action, string sourceFile, string targetPath, string targetFolder)
        {
            switch (action)
            {
                case ConflictAction.Overwrite:
                    return targetPath; // Will overwrite via CopyFileWithRetry (overwrite: true)

                case ConflictAction.Skip:
                    Debug.WriteLine($"[IMPORT] Skipping (user choice): {Path.GetFileName(sourceFile)}");
                    return null;

                case ConflictAction.Rename:
                    return FindNonCollidingName(targetPath);

                default:
                    return targetPath;
            }
        }

        /// <summary>
        /// Finds a non-colliding filename by appending (2), (3), etc.
        /// </summary>
        private static string FindNonCollidingName(string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath)!;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
            var ext = Path.GetExtension(targetPath);

            for (int n = 2; n < 1000; n++)
            {
                var candidate = Path.Combine(dir, $"{nameWithoutExt} ({n}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            // Extremely unlikely — fall back to overwrite
            return targetPath;
        }

        /// <summary>
        /// Checks if this show/album already exists in the library.
        /// Uses the universal folder name as the identity key.
        /// </summary>
        public bool ShowExistsInLibrary(string libraryRoot, AlbumInfo albumInfo)
        {
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                return false;
            }

            var artistFolder = SanitizeFolderName(albumInfo.Artist ?? "Unknown Artist");
            var albumFolder = BuildLibraryFolderName(albumInfo);
            var targetPath = Path.Combine(libraryRoot, artistFolder, albumFolder);

            return Directory.Exists(targetPath);
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
