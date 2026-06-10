using DeadEditor.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DeadEditor.Tests")]

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

        // Guards the post-load cache mutations (Evict / NotifySaved) and the paired
        // _sortedDates maintenance. Reads run on the WPF UI thread alongside the saves
        // and deletes that drive these mutations.
        private readonly object _writeLock = new();

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

        /// <summary>
        /// Test-only constructor: seeds the cache directly from in-memory concerts,
        /// bypassing all file I/O so the cache-coherence logic (Evict / NotifySaved)
        /// can be unit-tested without touching the real AppData concerts directory.
        /// Not used in production — production goes through the lazy singleton.
        /// </summary>
        internal ConcertLookupService(IEnumerable<ConcertReference> concerts)
        {
            _concerts = new Dictionary<string, ConcertReference>();
            foreach (var concert in concerts)
            {
                if (concert != null && !string.IsNullOrEmpty(concert.Date))
                    _concerts[concert.Date] = concert;
            }
            _sortedDates = _concerts.Keys.OrderBy(d => d).ToList();
            ConcertsPath = string.Empty;
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

        /// <summary>
        /// Reconciles the cache after a concert is saved through the setlist editor.
        /// In the shared-live-instance model the cached value is the very object the
        /// editor mutated, so the value is already current — what goes stale is the
        /// dictionary KEY when the date changes. If <paramref name="oldDate"/> differs
        /// from the saved concert's date, the old-date entry is evicted and the concert
        /// is re-inserted under its new date; <c>_sortedDates</c> is maintained either way.
        /// Idempotent: a missing old key is not an error (no throw).
        /// </summary>
        public void NotifySaved(string oldDate, ConcertReference concert)
        {
            if (concert == null || string.IsNullOrEmpty(concert.Date))
                return;

            var newDate = concert.Date;
            lock (_writeLock)
            {
                if (!string.Equals(oldDate, newDate, StringComparison.Ordinal))
                    RemoveLocked(oldDate);

                _concerts[newDate] = concert;
                InsertDateLocked(newDate);
            }
        }

        /// <summary>
        /// Removes the concert for <paramref name="date"/> from the cache, used by the
        /// delete path so a deleted concert no longer resolves from memory before restart.
        /// Idempotent: evicting a date not in the cache does nothing (no throw).
        /// </summary>
        public void Evict(string date)
        {
            if (string.IsNullOrEmpty(date))
                return;

            lock (_writeLock)
            {
                RemoveLocked(date);
            }
        }

        // Removes a date from both the dictionary and the sorted-date index, keeping them
        // in sync. Caller holds _writeLock.
        private void RemoveLocked(string date)
        {
            if (string.IsNullOrEmpty(date))
                return;

            if (_concerts.Remove(date))
            {
                var idx = _sortedDates.BinarySearch(date);
                if (idx >= 0)
                    _sortedDates.RemoveAt(idx);
            }
        }

        // Inserts a date into the sorted index in order if not already present. Uses the
        // same default string comparer as the initial OrderBy. Caller holds _writeLock.
        private void InsertDateLocked(string date)
        {
            var idx = _sortedDates.BinarySearch(date);
            if (idx < 0)
                _sortedDates.Insert(~idx, date);
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
