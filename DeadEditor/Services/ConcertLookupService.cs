using DeadEditor.Helpers;
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
    /// <summary>
    /// Outcome of <see cref="ConcertLookupService.PersistAliasSetlist"/>. Lets the commit-(iii)
    /// import/edit seam distinguish a fresh write from an idempotent no-op from a missing concert,
    /// so it can message correctly (silent on a dup; a notice on a date with no concert file).
    /// </summary>
    public enum AliasPersistResult
    {
        /// <summary>A new combine was appended and the concert file was rewritten.</summary>
        Persisted,

        /// <summary>The combine's covered-index set already existed — nothing written (idempotent).</summary>
        DuplicateNoOp,

        /// <summary>No concert is cached for the date — nothing written, no file fabricated.</summary>
        ConcertNotFound
    }

    public class ConcertLookupService
    {
        private static readonly Lazy<ConcertLookupService> _instance = new(() => new ConcertLookupService());
        public static ConcertLookupService Instance => _instance.Value;

        /// <summary>
        /// True once the singleton has been created (the ~2,300-file load has run). Reads the Lazy's
        /// <see cref="Lazy{T}.IsValueCreated"/>, which does NOT force the load — so callers can branch
        /// cold (first access this session, show a please-wait overlay and pre-warm off the UI thread)
        /// vs warm (already loaded, go straight to the fast path) without paying the load just to ask.
        /// </summary>
        public static bool IsLoaded => _instance.IsValueCreated;

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

        // Build-output bundled concerts: bin/<config>/<tfm>/Data/concerts/. In distributed mode this
        // is the first-run seed for AppData; in dev mode it is the fallback when no repo root can be
        // located by walking up for a .sln. Mirrors BoxSetService._buildOutputBoxSetsPath.
        private static readonly string _buildOutputConcertsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "concerts");

        // Lazy so the repo-walk fires once, only when BundledConcertsPath is first accessed. In dev
        // mode it resolves the repo-source Data/concerts/; in distributed mode it short-circuits to
        // the build-output path. Mirrors BoxSetService._bundledBoxSetsPath.
        private static readonly Lazy<string> _bundledConcertsPath = new(() =>
        {
            if (IsDevMode)
            {
                var repoSourcePath = TryResolveRepoSourceDataPath();
                if (repoSourcePath != null) return repoSourcePath;
            }
            return _buildOutputConcertsPath;
        });

        /// <summary>
        /// The bundled concerts location. In distributed mode (the default), the build-output
        /// directory next to the running exe, used as the first-run seed for AppData. In dev mode
        /// (<c>DEADEDITOR_DEV=1</c>), resolves to the repo-source <c>DeadEditor/Data/concerts/</c> if
        /// running from a checkout, so edits land in the repo and are immediately visible to
        /// <c>git status</c>. Falls back to the build-output path if no repo root can be located.
        /// Mirrors <c>BoxSetService.BundledBoxSetsPath</c>.
        /// </summary>
        public static string BundledConcertsPath => _bundledConcertsPath.Value;

        /// <summary>True when the <c>DEADEDITOR_DEV</c> environment variable is set to <c>"1"</c>.
        /// Read on every access (mirrors <c>BoxSetService.IsDevMode</c>).</summary>
        private static bool IsDevMode =>
            string.Equals(Environment.GetEnvironmentVariable("DEADEDITOR_DEV"), "1", StringComparison.Ordinal);

        /// <summary>
        /// The directory all concert reads and writes target in the current mode: repo-source
        /// <c>Data/concerts/</c> in dev mode (commit-ready), <see cref="AppDataConcertsPath"/>
        /// otherwise. The single switch governing BOTH the load (constructor below) and every
        /// write/delete/display site, so a dev-mode write and the in-memory cache cannot diverge.
        /// <c>DEADEDITOR_DEV</c> is a launch-time toggle that does not change mid-process, so the
        /// per-access env read here stays consistent with the load-time selection (the singleton
        /// loads once). Mirrors <c>BoxSetService.ActiveBoxSetsPath</c>.
        /// </summary>
        public static string ActiveConcertsPath => IsDevMode ? BundledConcertsPath : AppDataConcertsPath;

        /// <summary>
        /// In dev mode, resolves the repo-source <c>Data/concerts/</c> directory by walking up from
        /// the running exe's <see cref="AppDomain.CurrentDomain.BaseDirectory"/> looking for a
        /// directory containing a <c>.sln</c> file. Returns null if no such root is found (caller
        /// falls back to the build-output path). Fail-closed: a found <c>.sln</c> whose expected
        /// project layout is absent returns null rather than guessing. Mirrors
        /// <c>BoxSetService.TryResolveRepoSourceDataPath</c>.
        /// </summary>
        private static string? TryResolveRepoSourceDataPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0)
                {
                    var candidate = Path.Combine(dir.FullName, "DeadEditor", "Data", "concerts");
                    if (Directory.Exists(candidate))
                    {
                        Debug.WriteLine($"[CONCERTS] Dev mode resolved to repo source: {candidate}");
                        return candidate;
                    }
                    // sln found but the expected project layout isn't there — fail closed.
                    return null;
                }
                dir = dir.Parent;
            }
            return null;
        }

        private ConcertLookupService()
        {
            var sw = Stopwatch.StartNew();

            // Dev-aware path selection (the single ActiveConcertsPath switch). DEADEDITOR_DEV is a
            // launch-time toggle that does not change mid-process, so the per-access env read in
            // ActiveConcertsPath (used by the write/delete sites) stays consistent with this
            // load-time selection — the singleton loads once.
            if (IsDevMode)
            {
                // Dev mode: read AND write repo-source Data/concerts/ directly (commit-ready),
                // bypassing AppData entirely — NO first-run seeding (the dev no-op, mirroring
                // BoxSetService.EnsureInitialized returning early in dev mode).
                ConcertsPath = ActiveConcertsPath;
            }
            else
            {
                var appDataDir = AppDataConcertsPath;
                var bundledDir = _buildOutputConcertsPath;

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

        /// <summary>
        /// Persists a confirmed combine alias into the concert authority file (seam (b),
        /// alias-setlists-spec.md §4). The combine is keyed by performance <paramref name="date"/>
        /// alone; <paramref name="aliasEntry"/> carries only the covered official indices. Appends
        /// to the single shared <see cref="AliasSetlist"/> for the concert (created on first alias),
        /// idempotent by covered-index set, then writes atomically to
        /// <see cref="ActiveConcertsPath"/>/{date}.json (dev-aware, commit-ready under
        /// <c>DEADEDITOR_DEV</c>). The in-memory append is transactional: a failed disk write rolls
        /// the append back so the cache never holds an alias that is not on disk, then rethrows
        /// (the UI seam in commit (iii) catches and messages — mirrors <c>BoxSetService.Write</c>).
        /// No fabrication: an unknown date returns <see cref="AliasPersistResult.ConcertNotFound"/>
        /// without creating a file (§4/§6). Returns <see cref="AliasPersistResult.DuplicateNoOp"/>
        /// for an idempotent re-confirm — nothing is written, the file's mtime is untouched.
        /// SYNC (matches <c>BoxSetService.Write</c> and the rest of this service); a UI-thread
        /// caller wraps in <c>Task.Run</c>.
        /// </summary>
        public AliasPersistResult PersistAliasSetlist(string date, AliasEntry aliasEntry)
        {
            // Non-throwing guards, matching GetConcertByDate / Evict / NotifySaved.
            if (string.IsNullOrEmpty(date) || aliasEntry == null)
                return AliasPersistResult.ConcertNotFound;

            // The cache holds the live instance; null means no concert file for this date — do NOT
            // fabricate one (spec §4/§6). GetConcertByDate does not take _writeLock, so calling it
            // before acquiring the lock below is not re-entrant.
            var concert = GetConcertByDate(date);
            if (concert == null)
                return AliasPersistResult.ConcertNotFound;

            // In-memory dedup + append under the cache lock (consistent with the other post-load
            // cache mutations). The disk I/O is deliberately OUTSIDE the lock — file I/O must not be
            // held across the lock that the UI thread's reads contend on.
            bool containerCreated;
            lock (_writeLock)
            {
                bool hadContainer = concert.AliasSetlists.Count > 0;
                if (!TryAppendAliasEntry(concert, aliasEntry))
                    return AliasPersistResult.DuplicateNoOp; // idempotent — skip the write entirely
                containerCreated = !hadContainer;
            }

            try
            {
                // Serialize the now-mutated live concert and write atomically (temp + delete +
                // rename) to the dev-aware path. Same pattern as EditSetlistView / BoxSetService.
                var json = CanonicalJson.Serialize(concert);
                Directory.CreateDirectory(ActiveConcertsPath);

                var targetPath = Path.Combine(ActiveConcertsPath, $"{date}.json");
                var tempPath = targetPath + ".tmp";

                File.WriteAllText(tempPath, json);
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch
            {
                // Transactional rollback: remove the just-appended entry (by reference), and the
                // container too if THIS call created it and it is now empty — so the cache never
                // holds an alias that is not on disk. Then rethrow for the UI seam to surface.
                lock (_writeLock)
                {
                    var target = concert.AliasSetlists.FirstOrDefault();
                    if (target != null)
                    {
                        target.Entries.Remove(aliasEntry);
                        if (containerCreated && target.Entries.Count == 0)
                            concert.AliasSetlists.Remove(target);
                    }
                }
                throw;
            }

            // Date is unchanged, so this is a cache no-op; retained for symmetry with the
            // EditSetlistView save pattern.
            NotifySaved(date, concert);
            return AliasPersistResult.Persisted;
        }

        /// <summary>
        /// Pure in-memory append/dedup for a confirmed combine alias — ZERO I/O. Finds the single
        /// shared <see cref="AliasSetlist"/> for the concert (alias-setlists-spec.md §4); if any
        /// existing entry's <see cref="AliasEntry.CoveredOfficialIndices"/> is order-sensitive
        /// <see cref="System.Linq.Enumerable.SequenceEqual{T}(IEnumerable{T}, IEnumerable{T})"/> to
        /// <paramref name="entry"/>'s (covered runs are contiguous ascending, so order is identity),
        /// returns <c>false</c> with no mutation. Otherwise creates the <see cref="AliasSetlist"/>
        /// if absent (Id/Label left as default provenance values — NOT discriminators), appends
        /// <paramref name="entry"/>, and returns <c>true</c>. Internal so the cache/dedup logic is
        /// unit-testable without touching disk (via InternalsVisibleTo).
        /// </summary>
        internal static bool TryAppendAliasEntry(ConcertReference concert, AliasEntry entry)
        {
            var target = concert.AliasSetlists.FirstOrDefault();

            if (target != null &&
                target.Entries.Any(e =>
                    e.CoveredOfficialIndices.SequenceEqual(entry.CoveredOfficialIndices)))
            {
                return false; // dedup: this covered run is already recorded
            }

            if (target == null)
            {
                target = new AliasSetlist();
                concert.AliasSetlists.Add(target);
            }

            target.Entries.Add(entry);
            return true;
        }

        /// <summary>
        /// Pure in-memory removal mirror of <see cref="TryAppendAliasEntry"/> — ZERO I/O. Finds the
        /// single shared <see cref="AliasSetlist"/> for the concert (alias-setlists-spec.md §4) and
        /// removes the entry whose <see cref="AliasEntry.CoveredOfficialIndices"/> is order-sensitive
        /// <see cref="System.Linq.Enumerable.SequenceEqual{T}(IEnumerable{T}, IEnumerable{T})"/> to
        /// <paramref name="coveredIndices"/> (covered runs are contiguous ascending, so order is the
        /// same identity append dedups on). Returns <c>false</c> with no mutation when the concert has
        /// no alias container, the run is absent (idempotent no-op), or the inputs are null. When the
        /// removed entry was the container's last, the empty <see cref="AliasSetlist"/> is dropped from
        /// <see cref="ConcertReference.AliasSetlists"/> rather than left present-but-empty: an
        /// empty-but-present container would serialize <c>"aliasSetlists":[{...,"entries":[]}]</c> noise
        /// (<see cref="ConcertReference.ShouldSerializeAliasSetlists"/> only omits a <c>Count == 0</c>
        /// list, and there is no <c>ShouldSerialize</c> on the container/entries), so dropping keeps an
        /// otherwise-pristine concert file byte-clean — the same drop-when-empty
        /// <see cref="PersistAliasSetlist"/>'s rollback already models. Returns <c>true</c> on a real
        /// removal. Internal so the drop/dedup logic is unit-testable without touching disk (via
        /// InternalsVisibleTo).
        /// </summary>
        internal static bool TryRemoveAliasEntry(ConcertReference concert, IReadOnlyList<int> coveredIndices)
        {
            if (concert?.AliasSetlists == null || coveredIndices == null)
                return false;

            var target = concert.AliasSetlists.FirstOrDefault();
            if (target?.Entries == null)
                return false;

            var entry = target.Entries.FirstOrDefault(e =>
                e.CoveredOfficialIndices.SequenceEqual(coveredIndices));
            if (entry == null)
                return false; // idempotent: this covered run is not recorded

            target.Entries.Remove(entry);

            // Drop the shared container when its last entry is removed (Q4) — keeps the concert file
            // byte-pristine; an empty-but-present container would serialize as aliasSetlists noise.
            if (target.Entries.Count == 0)
                concert.AliasSetlists.Remove(target);

            return true;
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
