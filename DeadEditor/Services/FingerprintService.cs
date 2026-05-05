using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DeadEditor.Services
{
    /// <summary>
    /// Result of a fingerprint precompute batch. Counts are mutually exclusive —
    /// every input track ends up in exactly one of Computed, SkippedExisting, or Failed.
    /// </summary>
    public record FingerprintBatchResult(
        int Computed,
        int SkippedExisting,
        int Failed,
        bool FpcalcAvailable);

    /// <summary>
    /// Shared fingerprint-precompute service. Used by both the import pipeline
    /// (which relies on a later WriteMetadataWithRetry pass to persist tags) and the
    /// Edit Metadata "Fingerprint" button (which writes tags per-track via the
    /// onTrackComplete callback).
    /// </summary>
    public class FingerprintService
    {
        private readonly MusicBrainzService _musicBrainzService;

        public FingerprintService(MusicBrainzService musicBrainzService)
        {
            _musicBrainzService = musicBrainzService;
        }

        /// <summary>
        /// Computes Chromaprint fingerprints for any tracks that don't already carry one.
        /// Stamps results onto <see cref="TrackInfo.AcoustIdFingerprint"/> in memory; disk
        /// writing is the caller's responsibility (pass <paramref name="onTrackComplete"/>
        /// to write tags as each fingerprint lands).
        /// </summary>
        /// <remarks>
        /// Existing fingerprint tags are trusted and not recomputed: a Chromaprint fingerprint
        /// is a deterministic function of the decoded audio bytes, so the stored value is
        /// correct as long as the audio hasn't been re-encoded externally.
        ///
        /// On the first fpcalc-unavailable failure (InvalidOperationException or
        /// FileNotFoundException from <see cref="MusicBrainzService.GetFingerprintAsync"/>),
        /// a sticky flag is set so subsequent tracks fail fast without retrying fpcalc.
        /// </remarks>
        public async Task<FingerprintBatchResult> PrecomputeFingerprintsAsync(
            IList<TrackInfo> tracks,
            IProgress<(int current, int total, string message)>? progress = null,
            Func<TrackInfo, Task>? onTrackComplete = null)
        {
            int computed = 0;
            int skippedExisting = 0;
            int failed = 0;
            bool fpcalcUnavailable = false;
            int total = tracks.Count;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];

                if (!string.IsNullOrEmpty(track.AcoustIdFingerprint))
                {
                    skippedExisting++;
                    continue;
                }

                if (fpcalcUnavailable)
                {
                    failed++;
                    continue;
                }

                progress?.Report((i + 1, total, $"Fingerprinting tracks ({i + 1} of {total})..."));

                try
                {
                    track.AcoustIdFingerprint = await _musicBrainzService.GetFingerprintAsync(track.FilePath);

                    if (string.IsNullOrEmpty(track.AcoustIdFingerprint))
                    {
                        failed++;
                    }
                    else
                    {
                        computed++;
                        if (onTrackComplete != null)
                        {
                            await onTrackComplete(track);
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"[FINGERPRINT] fpcalc unavailable: {ex.Message}");
                    progress?.Report((i + 1, total, "fpcalc not configured — skipping fingerprinting"));
                    fpcalcUnavailable = true;
                    failed++;
                }
                catch (FileNotFoundException ex)
                {
                    Debug.WriteLine($"[FINGERPRINT] fpcalc.exe not found: {ex.Message}");
                    progress?.Report((i + 1, total, "fpcalc.exe not found — skipping fingerprinting"));
                    fpcalcUnavailable = true;
                    failed++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FINGERPRINT] Failed for {track.FilePath}: {ex.Message}");
                    failed++;
                }
            }

            return new FingerprintBatchResult(
                Computed: computed,
                SkippedExisting: skippedExisting,
                Failed: failed,
                FpcalcAvailable: !fpcalcUnavailable);
        }

        /// <summary>
        /// Writes <see cref="TrackInfo.AcoustIdFingerprint"/> to the track's tag file
        /// (FLAC Xiph <c>ACOUSTID_FINGERPRINT</c> or MP3 TXXX <c>Acoustid Fingerprint</c>).
        /// No-op when the fingerprint is empty or the file is missing.
        /// </summary>
        public static void WriteFingerprintToTrackFile(TrackInfo track)
        {
            if (track == null) return;
            if (string.IsNullOrEmpty(track.AcoustIdFingerprint)) return;
            if (string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath)) return;

            PathGuard.EnsureWithinLibrary(track.FilePath, "FingerprintService.WriteFingerprintToTrackFile");

            try
            {
                using var file = TagLib.File.Create(track.FilePath);

                if (file is TagLib.Flac.File)
                {
                    var xiph = (TagLib.Ogg.XiphComment?)file.GetTag(TagLib.TagTypes.Xiph);
                    xiph?.SetField("ACOUSTID_FINGERPRINT", track.AcoustIdFingerprint);
                }
                else
                {
                    var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2, true);
                    if (id3v2 != null)
                    {
                        var existing = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "Acoustid Fingerprint", false);
                        if (existing != null) id3v2.RemoveFrame(existing);
                        var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "Acoustid Fingerprint", true);
                        frame.Text = new[] { track.AcoustIdFingerprint };
                    }
                }

                file.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FINGERPRINT] Error writing tag for {track.FilePath}: {ex.Message}");
            }
        }
    }
}
