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
        public void ImportToLibrary(string libraryRoot, AlbumInfo albumInfo, List<TrackInfo> tracks, IProgress<(int current, int total, string message)>? progress = null)
        {
            if (string.IsNullOrEmpty(libraryRoot))
            {
                throw new ArgumentException("Library root path is not set");
            }

            if (!Directory.Exists(libraryRoot))
            {
                Directory.CreateDirectory(libraryRoot);
            }

            // Group tracks by date (for multi-show imports)
            var tracksByDate = tracks.GroupBy(t => t.PerformanceDate ?? albumInfo.Date);
            var totalTracks = tracks.Count;
            var processedTracks = 0;

            foreach (var dateGroup in tracksByDate)
            {
                var date = dateGroup.Key;
                var dateTracks = dateGroup.ToList();

                // Create folder structure: LibraryRoot\Year\Date - Venue, City, State\
                var year = DateTime.Parse(date).Year.ToString();
                var folderName = $"{date} - {albumInfo.Venue}, {albumInfo.City}, {albumInfo.State}";

                // Sanitize folder name (remove invalid characters)
                folderName = SanitizeFolderName(folderName);

                var targetFolder = Path.Combine(libraryRoot, year, folderName);

                // Create the target folder
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // Copy and write metadata for each track
                foreach (var track in dateTracks)
                {
                    processedTracks++;
                    progress?.Report((processedTracks, totalTracks, $"Importing track {processedTracks} of {totalTracks}: {track.NormalizedTitle ?? track.Title}"));

                    // Generate new filename: "01 - Truckin' (1971-04-25).flac"
                    var trackTitle = track.NormalizedTitle ?? track.Title;
                    if (track.HasSegue)
                    {
                        trackTitle += " >";
                    }

                    var newFileName = $"{track.TrackNumber:D2} - {trackTitle} ({date}).flac";
                    newFileName = SanitizeFileName(newFileName);

                    var targetPath = Path.Combine(targetFolder, newFileName);

                    // Copy the file
                    File.Copy(track.FilePath, targetPath, overwrite: true);

                    // Update the track's file path temporarily for metadata writing
                    var originalPath = track.FilePath;
                    track.FilePath = targetPath;

                    // Write metadata to the copied file
                    try
                    {
                        using (var file = TagLib.File.Create(targetPath))
                        {
                            var trackDate = track.PerformanceDate ?? albumInfo.Date;
                            var title = track.NormalizedTitle ?? track.Title;

                            if (track.HasSegue)
                            {
                                title = title + " >";
                            }

                            file.Tag.Title = $"{title} ({trackDate})";
                            file.Tag.Album = albumInfo.AlbumTitle;
                            file.Tag.Performers = new[] { albumInfo.Artist };
                            file.Tag.AlbumArtists = new[] { albumInfo.Artist };
                            file.Tag.Track = (uint)track.TrackNumber;

                            if (DateTime.TryParse(albumInfo.Date, out var parsedDate))
                            {
                                file.Tag.Year = (uint)parsedDate.Year;
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
        }

        private string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidPathChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        private string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        /// <summary>
        /// Checks if this show already exists in the library
        /// </summary>
        public bool ShowExistsInLibrary(string libraryRoot, AlbumInfo albumInfo)
        {
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                return false;
            }

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
