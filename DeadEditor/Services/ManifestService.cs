using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        /// <summary>
        /// Generates a manifest from album info and tracks, writes it as a sidecar JSON file
        /// adjacent to the album folder. Defaults <c>verified</c> to false and
        /// <c>archivistNote</c> to empty — for callers (e.g. fresh imports) that have no
        /// verification surface.
        /// </summary>
        public void WriteManifest(string albumFolderPath, AlbumInfo albumInfo, List<TrackInfo> tracks)
        {
            WriteManifest(albumFolderPath, albumInfo, tracks, verified: false, archivistNote: "");
        }

        /// <summary>
        /// Generates a manifest from album info and tracks with explicit verification state.
        /// </summary>
        public void WriteManifest(string albumFolderPath, AlbumInfo albumInfo, List<TrackInfo> tracks,
            bool verified, string archivistNote)
        {
            var folderName = Path.GetFileName(albumFolderPath);
            var parentDir = Path.GetDirectoryName(albumFolderPath);

            if (string.IsNullOrEmpty(parentDir))
            {
                Debug.WriteLine($"[MANIFEST] Cannot determine parent directory for: {albumFolderPath}");
                return;
            }

            var manifestPath = Path.Combine(parentDir, folderName + ".json");
            PathGuard.EnsureWithinLibrary(manifestPath, "ManifestService.WriteManifest");

            var manifest = new AlbumManifest
            {
                Version = 2,
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
                Verified = verified,
                ArchivistNote = archivistNote ?? "",
                ManifestSavedAt = DateTime.UtcNow,
                Tracks = tracks.Select(t => new ManifestTrack
                {
                    Filename = Path.GetFileName(t.FilePath),
                    TrackNumber = t.TrackNumber,
                    DiscNumber = t.DiscNumber,
                    Title = t.DisplayTitle ?? t.Title ?? "",
                    SongName = t.SongName ?? "",
                    TrackDate = t.TrackDate ?? "",
                    Segue = t.Segue,
                    AcoustIdFingerprint = t.AcoustIdFingerprint ?? ""
                }).ToList()
            };

            try
            {
                var json = JsonConvert.SerializeObject(manifest, _jsonSettings);
                File.WriteAllText(manifestPath, json);
                Debug.WriteLine($"[MANIFEST] Written: {manifestPath} ({manifest.Tracks.Count} tracks, verified={verified})");
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
                var jObject = JObject.Parse(json);
                MigrateLegacyFields(jObject);
                return jObject.ToObject<AlbumManifest>(JsonSerializer.Create(_jsonSettings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MANIFEST] Error reading {manifestPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps legacy v1 fields onto the v2 schema in-place. Newtonsoft is
        /// case-insensitive on key lookup during deserialization, so PascalCase
        /// v1 keys (e.g. "VerifiedAt") and camelCase v1 keys (e.g. "verifiedAt")
        /// are both recognized. Defaults for new v2 fields fall out of the C#
        /// property initializers when the JSON omits them.
        /// </summary>
        private static void MigrateLegacyFields(JObject jObject)
        {
            JToken? FindKey(string camelCase)
            {
                foreach (var prop in jObject.Properties())
                {
                    if (string.Equals(prop.Name, camelCase, StringComparison.OrdinalIgnoreCase))
                        return prop.Value;
                }
                return null;
            }

            void RemoveKey(string camelCase)
            {
                var match = jObject.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, camelCase, StringComparison.OrdinalIgnoreCase));
                match?.Remove();
            }

            if (FindKey("manifestSavedAt") == null)
            {
                var legacy = FindKey("verifiedAt");
                if (legacy != null)
                {
                    RemoveKey("verifiedAt");
                    jObject["manifestSavedAt"] = legacy;
                }
            }
        }
    }
}
