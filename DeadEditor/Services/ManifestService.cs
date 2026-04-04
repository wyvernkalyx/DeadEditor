using DeadEditor.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    /// <summary>
    /// Generates and reads metadata manifest files (sidecar JSON) for imported albums.
    /// Manifests capture verified metadata state so it can be restored on re-import.
    /// </summary>
    public class ManifestService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Generates a manifest from album info and tracks, writes it as a sidecar JSON file
        /// adjacent to the album folder (e.g., "1971-02-19 - Capitol Theatre - Port Chester, NY.json").
        /// </summary>
        public void WriteManifest(string albumFolderPath, AlbumInfo albumInfo, List<TrackInfo> tracks)
        {
            var folderName = Path.GetFileName(albumFolderPath);
            var parentDir = Path.GetDirectoryName(albumFolderPath);

            if (string.IsNullOrEmpty(parentDir))
            {
                Debug.WriteLine($"[MANIFEST] Cannot determine parent directory for: {albumFolderPath}");
                return;
            }

            var manifest = new AlbumManifest
            {
                Version = 1,
                FolderName = folderName,
                AlbumName = albumInfo.AlbumName ?? "",
                AlbumType = albumInfo.Type.ToString(),
                Artist = albumInfo.Artist ?? "",
                Date = albumInfo.AlbumDate ?? "",
                Venue = albumInfo.Venue ?? "",
                City = albumInfo.City ?? "",
                State = albumInfo.State ?? "",
                Edition = albumInfo.Edition ?? "",
                OfficialRelease = albumInfo.OfficialRelease ?? "",
                VerifiedAt = DateTime.UtcNow,
                Tracks = tracks.Select(t => new ManifestTrack
                {
                    Filename = Path.GetFileName(t.FilePath),
                    TrackNumber = t.TrackNumber,
                    DiscNumber = t.DiscNumber,
                    Title = t.DisplayTitle ?? t.Title ?? "",
                    SongName = t.SongName ?? "",
                    TrackDate = t.TrackDate ?? "",
                    Segue = t.Segue
                }).ToList()
            };

            var manifestPath = Path.Combine(parentDir, folderName + ".json");

            try
            {
                var json = JsonConvert.SerializeObject(manifest, _jsonSettings);
                File.WriteAllText(manifestPath, json);
                Debug.WriteLine($"[MANIFEST] Written: {manifestPath} ({manifest.Tracks.Count} tracks)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MANIFEST] Error writing {manifestPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a manifest for a multi-folder album. Each folder gets its own manifest.
        /// </summary>
        public void WriteManifest(List<string> folderPaths, AlbumInfo albumInfo, List<TrackInfo> tracks)
        {
            foreach (var folderPath in folderPaths)
            {
                // Filter tracks belonging to this folder
                var folderTracks = tracks.Where(t =>
                    !string.IsNullOrEmpty(t.FilePath) &&
                    Path.GetDirectoryName(t.FilePath)?.Equals(folderPath, StringComparison.OrdinalIgnoreCase) == true
                ).ToList();

                if (folderTracks.Count > 0)
                {
                    WriteManifest(folderPath, albumInfo, folderTracks);
                }
            }
        }

        /// <summary>
        /// Reads a manifest file for the given album folder, if one exists.
        /// </summary>
        public AlbumManifest? ReadManifest(string albumFolderPath)
        {
            var folderName = Path.GetFileName(albumFolderPath);
            var parentDir = Path.GetDirectoryName(albumFolderPath);

            if (string.IsNullOrEmpty(parentDir))
                return null;

            var manifestPath = Path.Combine(parentDir, folderName + ".json");

            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var json = File.ReadAllText(manifestPath);
                return JsonConvert.DeserializeObject<AlbumManifest>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MANIFEST] Error reading {manifestPath}: {ex.Message}");
                return null;
            }
        }
    }
}
