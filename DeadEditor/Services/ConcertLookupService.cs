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
    /// Provides O(1) lookup of concert reference data (setlists, venues) by date.
    /// Loads per-concert JSON files on first access.
    ///
    /// Data path priority:
    ///   1. %APPDATA%/DeadEditor/concerts/  (primary — survives upgrades)
    ///   2. Data/concerts/ in app directory  (fallback — ships with build)
    ///
    /// On first run, if concerts exist in Data/ but not AppData, copies them over.
    /// Thread-safe, lazily loaded singleton. Once loaded, read-only.
    /// </summary>
    public class ConcertLookupService
    {
        private static readonly Lazy<ConcertLookupService> _instance = new(() => new ConcertLookupService());
        public static ConcertLookupService Instance => _instance.Value;

        private readonly Dictionary<string, ConcertReference> _concerts;
        private readonly List<string> _sortedDates;

        /// <summary>The directory concerts were loaded from.</summary>
        public string ConcertsPath { get; }

        /// <summary>The AppData concerts directory (for SetlistFetcher targeting).</summary>
        public static string AppDataConcertsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeadEditor", "concerts");

        private ConcertLookupService()
        {
            var sw = Stopwatch.StartNew();

            var appDataDir = AppDataConcertsPath;
            var bundledDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "concerts");

            // Determine which directory to load from
            bool appDataExists = Directory.Exists(appDataDir) &&
                                 Directory.GetFiles(appDataDir, "*.json").Length > 0;
            bool bundledExists = Directory.Exists(bundledDir) &&
                                 Directory.GetFiles(bundledDir, "*.json").Length > 0;

            if (!appDataExists && bundledExists)
            {
                // First run: copy bundled concerts to AppData
                CopyBundledToAppData(bundledDir, appDataDir);
                ConcertsPath = appDataDir;
            }
            else if (appDataExists)
            {
                ConcertsPath = appDataDir;
            }
            else if (bundledExists)
            {
                ConcertsPath = bundledDir;
            }
            else
            {
                ConcertsPath = appDataDir; // Will be empty but path is set
                Debug.WriteLine($"[CONCERTS] Warning: no concerts found in {appDataDir} or {bundledDir}");
            }

            _concerts = LoadConcerts(ConcertsPath);
            _sortedDates = _concerts.Keys.OrderBy(d => d).ToList();
            sw.Stop();
            Debug.WriteLine($"[CONCERTS] Loaded {_concerts.Count:N0} concerts from {ConcertsPath} in {sw.ElapsedMilliseconds}ms");
        }

        private static void CopyBundledToAppData(string sourceDir, string destDir)
        {
            try
            {
                Directory.CreateDirectory(destDir);
                var files = Directory.GetFiles(sourceDir, "*.json");
                Debug.WriteLine($"[CONCERTS] First run: copying {files.Length} concert files to {destDir}");

                foreach (var file in files)
                {
                    var destFile = Path.Combine(destDir, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CONCERTS] Error copying bundled concerts: {ex.Message}");
            }
        }

        private static Dictionary<string, ConcertReference> LoadConcerts(string concertsDir)
        {
            var result = new Dictionary<string, ConcertReference>();

            if (!Directory.Exists(concertsDir))
                return result;

            var files = Directory.GetFiles(concertsDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var concert = JsonConvert.DeserializeObject<ConcertReference>(json);
                    if (concert != null && !string.IsNullOrEmpty(concert.Date))
                    {
                        result[concert.Date] = concert;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CONCERTS] Error loading {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>Get a concert by yyyy-MM-dd date — O(1) dictionary lookup.</summary>
        public ConcertReference? GetConcertByDate(string date)
        {
            if (string.IsNullOrEmpty(date))
                return null;

            _concerts.TryGetValue(date, out var concert);
            return concert;
        }

        /// <summary>Check if a concert exists for a date.</summary>
        public bool HasConcert(string date)
        {
            return !string.IsNullOrEmpty(date) && _concerts.ContainsKey(date);
        }

        /// <summary>Get all concert dates, sorted ascending.</summary>
        public IReadOnlyList<string> GetAllDates() => _sortedDates;

        /// <summary>Get all concerts, sorted by date ascending.</summary>
        public IReadOnlyList<ConcertReference> GetAllConcerts()
        {
            return _sortedDates.Select(d => _concerts[d]).ToList();
        }

        /// <summary>Total number of concerts loaded.</summary>
        public int Count => _concerts.Count;

        /// <summary>
        /// Search concerts that contain a song matching the given name (case-insensitive substring).
        /// </summary>
        public IReadOnlyList<ConcertReference> SearchBySong(string songName)
        {
            if (string.IsNullOrWhiteSpace(songName))
                return Array.Empty<ConcertReference>();

            var query = songName.Trim();
            return _sortedDates
                .Select(d => _concerts[d])
                .Where(c => c.Tracks.Any(t =>
                    t.SongName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>
        /// Search concerts by venue name (case-insensitive substring).
        /// </summary>
        public IReadOnlyList<ConcertReference> SearchByVenue(string venue)
        {
            if (string.IsNullOrWhiteSpace(venue))
                return Array.Empty<ConcertReference>();

            var query = venue.Trim();
            return _sortedDates
                .Select(d => _concerts[d])
                .Where(c => c.Venue.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
