using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DeadEditor.Services
{
    /// <summary>
    /// Orchestrates MBID migration: iterates library albums, looks up MusicBrainz releases,
    /// presents candidates for user confirmation, writes MBID tags, and manages state persistence.
    /// </summary>
    public class MbidMigrationService
    {
        private readonly MusicBrainzService _musicBrainzService;
        private readonly LibrarySettings _librarySettings;
        private MigrationState _state;
        private bool _isPaused;
        private bool _isCancelled;

        public MbidMigrationService(MusicBrainzService musicBrainzService, LibrarySettings librarySettings)
        {
            _musicBrainzService = musicBrainzService;
            _librarySettings = librarySettings;
            _state = new MigrationState();
        }

        // Events for UI binding
        public event EventHandler<MigrationProgressEventArgs>? ProgressChanged;
        public event EventHandler<CandidateReviewEventArgs>? CandidateReviewRequested;
        public event EventHandler<MigrationCompleteEventArgs>? MigrationCompleted;

        /// <summary>
        /// Gets the state file path in %APPDATA%/DeadEditor/
        /// </summary>
        public static string StateFilePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "DeadEditor", "mbid-migration-state.json");
            }
        }

        /// <summary>
        /// Loads existing migration state from disk, if any.
        /// Returns null if no state file exists.
        /// </summary>
        public static MigrationState? LoadState()
        {
            var path = StateFilePath;
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<MigrationState>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes the state file (for "Start Fresh").
        /// </summary>
        public static void DeleteState()
        {
            var path = StateFilePath;
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Counts how many albums are already tagged vs. needing migration.
        /// </summary>
        public static (int total, int alreadyTagged, int needsMigration) GetAlbumCounts(List<LibraryShow> shows)
        {
            int total = shows.Count;
            int alreadyTagged = shows.Count(s => !string.IsNullOrEmpty(s.MusicBrainzReleaseId));
            return (total, alreadyTagged, total - alreadyTagged);
        }

        public void Pause() => _isPaused = true;
        public void Cancel()
        {
            _isCancelled = true;
            _isPaused = true;
        }

        /// <summary>
        /// Runs the migration on a list of LibraryShow entries.
        /// </summary>
        public async Task RunAsync(List<LibraryShow> shows, bool dryRun, bool resume)
        {
            _isPaused = false;
            _isCancelled = false;

            // Load or create state
            if (resume)
            {
                _state = LoadState() ?? new MigrationState();
            }
            else
            {
                _state = new MigrationState();
            }

            // If resuming a real run after a dry-run, keep the state entries for pre-selection
            // but mark the state as a real run now
            bool isDryRunToRealTransition = !dryRun && _state.DryRun && _state.Albums.Count > 0;

            _state.DryRun = dryRun;
            if (_state.StartedAt == default)
                _state.StartedAt = DateTime.UtcNow;

            var albumsToProcess = shows.Where(s => ShouldProcessAlbum(s, isDryRunToRealTransition)).ToList();
            int totalToProcess = albumsToProcess.Count;
            int completed = 0;
            int matched = 0;
            int skipped = 0;
            int failed = 0;
            int alreadyTagged = shows.Count(s => !string.IsNullOrEmpty(s.MusicBrainzReleaseId));
            var failures = new List<(string folderPath, string reason)>();

            // Count already-completed from state (for resume of real run)
            if (resume && !isDryRunToRealTransition)
            {
                completed = _state.Albums.Count(a => a.Value.Status != null);
                matched = _state.Albums.Count(a => a.Value.Status == "matched");
                skipped = _state.Albums.Count(a => a.Value.Status == "skipped");
                failed = _state.Albums.Count(a => a.Value.Status == "failed");
            }

            int totalForProgress = totalToProcess + completed;

            foreach (var show in albumsToProcess)
            {
                if (_isPaused) break;

                var folderKey = show.FolderPaths.FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(folderKey)) continue;

                // Check if folder still exists
                if (!Directory.Exists(folderKey))
                {
                    failed++;
                    completed++;
                    failures.Add((folderKey, "Folder no longer exists"));
                    UpdateState(folderKey, "failed", null, "Folder no longer exists");
                    RaiseProgress(show, completed, totalForProgress, matched, skipped, failed, alreadyTagged);
                    continue;
                }

                RaiseProgress(show, completed, totalForProgress, matched, skipped, failed, alreadyTagged);

                // Look for dry-run pre-selection
                string? dryRunMbid = null;
                if (isDryRunToRealTransition && _state.Albums.TryGetValue(folderKey, out var dryRunEntry))
                {
                    if (dryRunEntry.Status == "matched" && !string.IsNullOrEmpty(dryRunEntry.Mbid))
                        dryRunMbid = dryRunEntry.Mbid;
                }

                // Attempt fingerprint lookup
                List<ReleaseOption>? candidates = null;
                bool fpcalcAvailable = !string.IsNullOrEmpty(_librarySettings.FpcalcPath) &&
                                       File.Exists(_librarySettings.FpcalcPath);
                string? lookupWarning = null;

                if (fpcalcAvailable)
                {
                    try
                    {
                        // Build TrackInfo list for fingerprinting
                        var tracks = BuildTrackInfoList(show);
                        if (tracks.Count > 0)
                        {
                            candidates = await _musicBrainzService.LookupAllReleasesAsync(tracks, show.TrackCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MBID Migration] Fingerprint error for {folderKey}: {ex.Message}");
                        lookupWarning = $"Fingerprint lookup failed: {ex.Message}";
                    }
                }
                else
                {
                    lookupWarning = "fpcalc not configured — using name search only";
                }

                // Fallback: name search
                if ((candidates == null || candidates.Count == 0) && !_isPaused)
                {
                    try
                    {
                        var albumName = !string.IsNullOrEmpty(show.AlbumName) ? show.AlbumName
                            : !string.IsNullOrEmpty(show.OfficialRelease) ? show.OfficialRelease
                            : Path.GetFileName(folderKey);
                        var artistName = _librarySettings.PrimaryArtistName;
                        var year = show.ReleaseYear?.ToString();

                        var nameResults = await _musicBrainzService.SearchReleasesByNameAsync(albumName, artistName, year);
                        if (nameResults != null && nameResults.Count > 0)
                        {
                            if (candidates == null)
                                candidates = nameResults;
                            else
                                candidates.AddRange(nameResults);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MBID Migration] Name search error for {folderKey}: {ex.Message}");
                    }
                }

                if (_isPaused) break;

                // Deduplicate candidates by ReleaseId
                if (candidates != null)
                {
                    candidates = candidates
                        .GroupBy(c => c.ReleaseId)
                        .Select(g => g.First())
                        .ToList();
                }

                // Request user review via event
                var reviewArgs = new CandidateReviewEventArgs(
                    show, candidates ?? new List<ReleaseOption>(), dryRunMbid, lookupWarning);

                CandidateReviewRequested?.Invoke(this, reviewArgs);

                // Wait for user response (set by UI via the event args)
                while (!reviewArgs.IsResolved && !_isCancelled)
                {
                    await Task.Delay(100);
                }

                if (_isCancelled) break;

                completed++;

                if (reviewArgs.UserAction == CandidateAction.Skip)
                {
                    skipped++;
                    UpdateState(folderKey, "skipped", null, null);
                }
                else if (reviewArgs.UserAction == CandidateAction.Confirm && !string.IsNullOrEmpty(reviewArgs.SelectedMbid))
                {
                    if (!dryRun)
                    {
                        try
                        {
                            WriteMbidToAlbum(show, reviewArgs.SelectedMbid);
                            show.MusicBrainzReleaseId = reviewArgs.SelectedMbid;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failures.Add((folderKey, $"Write error: {ex.Message}"));
                            UpdateState(folderKey, "failed", null, $"Write error: {ex.Message}");
                            RaiseProgress(show, completed, totalForProgress, matched, skipped, failed, alreadyTagged);
                            continue;
                        }
                    }
                    matched++;
                    UpdateState(folderKey, "matched", reviewArgs.SelectedMbid, null);
                }
                else
                {
                    failed++;
                    failures.Add((folderKey, "No candidates found"));
                    UpdateState(folderKey, "failed", null, "No candidates found");
                }

                RaiseProgress(show, completed, totalForProgress, matched, skipped, failed, alreadyTagged);
            }

            MigrationCompleted?.Invoke(this, new MigrationCompleteEventArgs(
                matched, skipped, alreadyTagged, failed, failures, dryRun, _isPaused && !_isCancelled));
        }

        private bool ShouldProcessAlbum(LibraryShow show, bool isDryRunToRealTransition)
        {
            // Already tagged in audio files — skip
            if (!string.IsNullOrEmpty(show.MusicBrainzReleaseId))
                return false;

            var folderKey = show.FolderPaths.FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(folderKey))
                return false;

            // Check state file
            if (_state.Albums.TryGetValue(folderKey, out var entry))
            {
                // Dry-run entries are re-processed in a real run (for pre-selection)
                if (isDryRunToRealTransition)
                    return true;

                // Real-run completed entries are skipped
                return false;
            }

            return true;
        }

        private List<TrackInfo> BuildTrackInfoList(LibraryShow show)
        {
            var tracks = new List<TrackInfo>();
            foreach (var folder in show.FolderPaths)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var audioFiles = Directory.GetFiles(folder, "*.flac")
                        .Concat(Directory.GetFiles(folder, "*.mp3"))
                        .OrderBy(f => f)
                        .ToArray();

                    int discNumber = show.FolderPaths.IndexOf(folder) + 1;
                    int trackNumber = 1;

                    foreach (var filePath in audioFiles)
                    {
                        tracks.Add(new TrackInfo
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            DiscNumber = discNumber,
                            TrackNumber = trackNumber++
                        });
                    }
                }
                catch { }
            }
            return tracks;
        }

        private void WriteMbidToAlbum(LibraryShow show, string mbid)
        {
            foreach (var folder in show.FolderPaths)
            {
                if (!Directory.Exists(folder)) continue;

                var audioFiles = Directory.GetFiles(folder, "*.flac")
                    .Concat(Directory.GetFiles(folder, "*.mp3"));

                foreach (var filePath in audioFiles)
                {
                    using var file = TagLib.File.Create(filePath);

                    if (file is TagLib.Flac.File flacFile)
                    {
                        var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                        xiph?.SetField("MUSICBRAINZ_ALBUMID", mbid);
                    }
                    else
                    {
                        var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2, true);
                        if (id3v2 != null)
                        {
                            // Remove existing frame if present, then add new
                            var existing = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", false);
                            if (existing != null)
                                id3v2.RemoveFrame(existing);

                            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", true);
                            frame.Text = new[] { mbid };
                        }
                    }

                    file.Save();
                }
            }
        }

        private void UpdateState(string folderKey, string status, string? mbid, string? reason)
        {
            _state.Albums[folderKey] = new MigrationAlbumEntry
            {
                Status = status,
                Mbid = mbid,
                Reason = reason,
                CompletedAt = DateTime.UtcNow
            };

            SaveStateAtomically();
        }

        private void SaveStateAtomically()
        {
            var path = StateFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            var json = JsonConvert.SerializeObject(_state, Formatting.Indented);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }

        private void RaiseProgress(LibraryShow currentAlbum, int completed, int total,
            int matched, int skipped, int failed, int alreadyTagged)
        {
            ProgressChanged?.Invoke(this, new MigrationProgressEventArgs(
                currentAlbum, completed, total, matched, skipped, failed, alreadyTagged));
        }
    }

    // ===== State Model =====

    public class MigrationState
    {
        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("dryRun")]
        public bool DryRun { get; set; }

        [JsonProperty("albums")]
        public Dictionary<string, MigrationAlbumEntry> Albums { get; set; } = new();
    }

    public class MigrationAlbumEntry
    {
        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("mbid")]
        public string? Mbid { get; set; }

        [JsonProperty("reason")]
        public string? Reason { get; set; }

        [JsonProperty("completedAt")]
        public DateTime CompletedAt { get; set; }
    }

    // ===== Event Args =====

    public class MigrationProgressEventArgs : EventArgs
    {
        public LibraryShow CurrentAlbum { get; }
        public int Completed { get; }
        public int Total { get; }
        public int Matched { get; }
        public int Skipped { get; }
        public int Failed { get; }
        public int AlreadyTagged { get; }

        public MigrationProgressEventArgs(LibraryShow currentAlbum, int completed, int total,
            int matched, int skipped, int failed, int alreadyTagged)
        {
            CurrentAlbum = currentAlbum;
            Completed = completed;
            Total = total;
            Matched = matched;
            Skipped = skipped;
            Failed = failed;
            AlreadyTagged = alreadyTagged;
        }
    }

    public enum CandidateAction
    {
        None,
        Confirm,
        Skip
    }

    public class CandidateReviewEventArgs : EventArgs
    {
        public LibraryShow Album { get; }
        public List<ReleaseOption> Candidates { get; }
        public string? DryRunPreSelectedMbid { get; }
        public string? Warning { get; }

        // Set by UI
        public bool IsResolved { get; set; }
        public CandidateAction UserAction { get; set; }
        public string? SelectedMbid { get; set; }

        public CandidateReviewEventArgs(LibraryShow album, List<ReleaseOption> candidates,
            string? dryRunPreSelectedMbid, string? warning)
        {
            Album = album;
            Candidates = candidates;
            DryRunPreSelectedMbid = dryRunPreSelectedMbid;
            Warning = warning;
        }
    }

    public class MigrationCompleteEventArgs : EventArgs
    {
        public int Matched { get; }
        public int Skipped { get; }
        public int AlreadyTagged { get; }
        public int Failed { get; }
        public List<(string folderPath, string reason)> Failures { get; }
        public bool WasDryRun { get; }
        public bool WasPaused { get; }

        public MigrationCompleteEventArgs(int matched, int skipped, int alreadyTagged, int failed,
            List<(string folderPath, string reason)> failures, bool wasDryRun, bool wasPaused)
        {
            Matched = matched;
            Skipped = skipped;
            AlreadyTagged = alreadyTagged;
            Failed = failed;
            Failures = failures;
            WasDryRun = wasDryRun;
            WasPaused = wasPaused;
        }
    }
}
