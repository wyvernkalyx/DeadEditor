# Fingerprint Persistence — Commit 2a Spec

## 1. Goal

Make the Chromaprint fingerprint a first-class, persistent property of every imported track. Today the fingerprint is computed inside `MusicBrainzService.LookupAllReleasesAsync` (4 tracks per album, transient, discarded after the AcoustID lookup). After this commit, every track imported via "Import to Library" carries `ACOUSTID_FINGERPRINT` in its tags, and existing fingerprint tags are read back into memory at folder load.

This commit deliberately stops short of:

- Computing `ACOUSTID_ID` (requires AcoustID API call per track — out of scope; future commit)
- Parallelizing `fpcalc` invocations (Commit 2b)
- Triggering fingerprinting from inside `MetadataService.WriteMetadata` — that path only writes the cached value
- Track-level recording MBIDs

## 2. Tag Schema

### FLAC (Vorbis comments)

- **Field name:** `ACOUSTID_FINGERPRINT`
- **Value:** Raw Chromaprint fingerprint string (URL-safe base64-like output from `fpcalc`, single line)
- **Read via:** `xiph.GetFirstField("ACOUSTID_FINGERPRINT")`
- **Write via:** `xiph.SetField("ACOUSTID_FINGERPRINT", fingerprint)` — only when the in-memory value is non-empty

### MP3 (ID3v2 TXXX)

- **Frame description:** `Acoustid Fingerprint`
- **Value:** Same Chromaprint fingerprint string
- **Read via:** `UserTextInformationFrame.Get(id3v2, "Acoustid Fingerprint", false)`
- **Write via:** `SetId3v2TextField(id3v2, "Acoustid Fingerprint", fingerprint)` — only when non-empty

### Read scope

- Read is **per-track** — every track gets its own fingerprint (unlike MBID which is shared across an album).
- The value lives on `TrackInfo.AcoustIdFingerprint` (string?, plain auto-property, no `INotifyPropertyChanged`).
- Populated by `MetadataService.ReadFolder` from the same `TagLib.File` instance that reads track number, title, etc. — zero new I/O.

## 3. Pre-compute Step (Import only)

`LibraryImportService.ImportToLibrary` runs a fingerprint pre-step **before** the per-track copy/write loop:

1. Validate `LibrarySettings.FpcalcPath` — if empty or pointing at a missing file, skip the entire step. Report `"fpcalc not configured — skipping fingerprinting"` via the existing `IProgress` channel and surface a final-status note. Import continues without fingerprint writes.
2. For each track in source order, **trust existing values**: if `track.AcoustIdFingerprint` is non-empty, skip — fingerprint is deterministic from audio bytes and recomputing on every import is wasteful. Otherwise invoke `MusicBrainzService.GetFingerprintAsync(track.FilePath).GetAwaiter().GetResult()` and stamp the result on `track.AcoustIdFingerprint`.
3. Per-track failures (one file unreadable, fpcalc crashed on it) are caught, logged, and the track is left with an empty fingerprint. The import continues; the failure count is included in the final status.
4. Report progress via the existing `IProgress<(int current, int total, string message)>` reporter, using the message format `Fingerprinting tracks (N of M)...` so the existing `StatusTextBlock` displays it without UI changes.

The pre-step is **sequential** — no parallelization in this commit. `fpcalc` invocations block on `.GetAwaiter().GetResult()`. This is acceptable because `ImportToLibrary` is already invoked from a `Task.Run` on a background thread ([ImportView.xaml.cs:1597](Views/ImportView.xaml.cs#L1597)).

## 4. Write Path

### `LibraryImportService.WriteMetadataWithRetry`

After the existing custom-field writes (`ALBUMDATE`, `VENUE`, etc.), and after the existing `MUSICBRAINZ_ALBUMID` write, write the cached fingerprint **guarded by `!string.IsNullOrEmpty(track.AcoustIdFingerprint)`**:

```csharp
if (!string.IsNullOrEmpty(track.AcoustIdFingerprint))
    xiph.SetField("ACOUSTID_FINGERPRINT", track.AcoustIdFingerprint);
```

The guard preserves any existing tag value when the import has nothing new to write (matches the existing MBID guard pattern verified in `LibraryImportServiceMbidTests.WriteMetadata_WithoutMbid_PreservesExistingMbidTag`).

### `MetadataService.WriteMetadata` (in-place)

Mirror the same guarded write — but **do not trigger fingerprinting from inside `WriteMetadata`**. The pre-compute step lives only in `LibraryImportService.ImportToLibrary`. `WriteMetadata` is the in-place button path used by ImportView's "Write Metadata"; it writes whatever is in `track.AcoustIdFingerprint` and leaves files alone when the field is empty.

This keeps the parity with the MBID pattern from Commit 1.6: in-place writes don't originate new fingerprint values, but they don't drop existing ones either.

## 5. fpcalc Unavailability

`LibrarySettings.FpcalcPath` defaults to `""`. If unset or the file is missing:

- The pre-compute step is skipped entirely.
- A `"fpcalc not configured — skipping fingerprinting"` status is reported.
- The final import status appends `"(N tracks not fingerprinted — fpcalc not configured)"` so the user notices.
- No persistent flag — re-running import after configuring fpcalc fills in the gaps (because tracks still have empty `AcoustIdFingerprint`).

This is deliberately a non-blocking warning. Fingerprinting is value-add; blocking the import would punish users who haven't configured fpcalc yet.

## 6. API Changes

### `MusicBrainzService.GetFingerprintAsync` — visibility

Changed from `private` to `public`. The method has no instance state beyond `_librarySettings.FpcalcPath`, and exposing it as a building block is justified by the import pipeline's need.

### `LibraryImportService` constructor

Adds a `MusicBrainzService` parameter:

```csharp
// Before:
public LibraryImportService(MetadataService metadataService)

// After:
public LibraryImportService(MetadataService metadataService, MusicBrainzService musicBrainzService)
```

Three call sites updated:
- `Views/ImportView.xaml.cs:85`
- `DeadEditor.Tests/LibraryImportServiceMbidTests.cs` (two test instances)

## 7. Tests

Tag round-trip tests only. End-to-end `fpcalc` tests are deferred until real-audio fixtures exist — the `CreateMinimalFlacFile` helper produces ~140 bytes of zero-padded data with no decodable audio frames, so `fpcalc` cannot generate a fingerprint from it.

Four new tests, parallel to the MBID pair on both paths:

| Test | What it verifies |
|---|---|
| `LibraryImportServiceFingerprintTests.ImportToLibrary_WithFingerprintSupplied_WritesFingerprintToFlacXiph` | When `TrackInfo.AcoustIdFingerprint` is set on the input, the imported file carries `ACOUSTID_FINGERPRINT` |
| `LibraryImportServiceFingerprintTests.ImportToLibrary_WithoutFingerprint_PreservesExistingFingerprintTag` | When source file is pre-tagged with a fingerprint and `TrackInfo.AcoustIdFingerprint` is null, the imported file's fingerprint tag is untouched |
| `MetadataServiceFingerprintTests.WriteMetadata_WithFingerprintSupplied_WritesFingerprintToFlacXiph` | In-place `WriteMetadata` writes `track.AcoustIdFingerprint` to the tag |
| `MetadataServiceFingerprintTests.WriteMetadata_WithoutFingerprint_PreservesExistingFingerprintTag` | In-place `WriteMetadata` leaves an existing fingerprint tag untouched when `track.AcoustIdFingerprint` is null |

## 8. Out of Scope (deferred)

- `ACOUSTID_ID` field (requires AcoustID API call — future commit)
- Parallel `fpcalc` invocations (Commit 2b)
- "Re-fingerprint" affordance to force recomputation when audio is re-encoded externally
- End-to-end `fpcalc` tests with real audio fixtures
- Library Grid badges or per-row fingerprint indicators (deferred to Commit 3.5)

## 9. UI Surfacing (Commit 3)

Fingerprint state is shown in two places, with **deliberately asymmetric treatment** because the data is per-track and the two surfaces operate at different levels of granularity. There is no single "the album's fingerprint" to display.

### 9.1 Track Info dialog (per-track)

The Track Info dialog ([TrackInfoDialog.xaml.cs](../TrackInfoDialog.xaml.cs)) shows one track's metadata. A new field row is added after the existing `Release MBID` row in both the FLAC branch and the MP3 branch:

- **Label:** `Acoustid Fingerprint`
- **Value source:** read directly from disk via `xiph.GetFirstField("ACOUSTID_FINGERPRINT")` (FLAC) or the `Acoustid Fingerprint` TXXX frame (MP3) — same disk-read pattern used for every other field in this dialog.
- **Display:** truncated via `TrackInfo.FormatFingerprintForDisplay` — first 12 chars + horizontal-ellipsis when length > 12; full value when ≤ 12; em-dash when null/empty/whitespace.
- **Copy button:** copies the **full** raw fingerprint, not the truncated display. Implemented via a small `AddField(label, displayValue, clipboardValue)` overload that decouples the visible string from the clipboard payload. The button is suppressed when the raw fingerprint is empty.

### 9.2 Edit Metadata view (album-level summary)

The Edit Metadata view ([Views/EditMetadataView.xaml](../Views/EditMetadataView.xaml)) operates at album level, so showing one fingerprint in its sidebar would misrepresent the data. Instead, the sidebar carries a **coverage summary** beneath the existing MBID row:

- **Label:** `FINGERPRINTS`
- **Three states**, computed in `RefreshUI` by counting `_tracks` where `Track.AcoustIdFingerprint` is not null/whitespace:
  - `All tracks fingerprinted` — every track has a fingerprint
  - `N / M tracks fingerprinted` — partial coverage (`0 < N < M`)
  - `No fingerprints` — none of the tracks have a fingerprint
- **Value source:** in-memory `_tracks` collection only. No tag re-reads from disk — `MetadataService.ReadFolder` already populates `TrackInfo.AcoustIdFingerprint` at folder-load per § 2.
- **No copy button** — there is no single value to copy. Users who need a specific track's fingerprint open Track Info for that track.

### 9.3 Truncation helper

`TrackInfo.FormatFingerprintForDisplay(string?)` ([Models/TrackInfo.cs](../Models/TrackInfo.cs)) is the single source of truth for the truncation rule. Edit Metadata's summary does not use it (no truncation needed — the summary is plain English), but the Track Info dialog uses it directly.

### 9.4 Deferred

- Library Grid per-row fingerprint badges — deferred to Commit 3.5 because they touch scan-time performance.
- "Verify fingerprint" affordance (recompute and compare) — queued, not in this commit.
- Editable fingerprint field — fingerprints are derived from audio bytes, so user-editable display would be misleading.

## 10. Fingerprint Tracks Button (Commit 3.6)

Albums imported before Commit 2a have no fingerprints. The `🎵 Fingerprint` button in the Edit Metadata sidebar lets users back-fill fingerprints for an existing library album.

### 10.1 Scope

- **Missing-only.** Tracks that already have a non-empty `AcoustIdFingerprint` are skipped. There is no "force re-fingerprint" mode in this commit.
- **Live disk commit.** Each fingerprint is written to the track's tag immediately after computation, not staged for Save Changes. Partial completion survives a mid-run crash because the next click resumes (skip-existing handles it).
- **No cancellation.** The user cannot interrupt a run mid-batch. Exiting the view while a run is in progress is unsupported behavior; the run continues until completion.
- **Sequential.** No `fpcalc` parallelization in this commit (deferred to Commit 2b).
- **No bulk affordance.** This button operates on the single album in the open Edit Metadata view. A library-wide "fingerprint all albums" button is deferred (Option 2 from the Commit 3.6 conversation).

### 10.2 Shared `FingerprintService`

The fingerprint precompute logic is extracted from `LibraryImportService` into [Services/FingerprintService.cs](../Services/FingerprintService.cs):

```csharp
public record FingerprintBatchResult(
    int Computed,        // newly fingerprinted
    int SkippedExisting, // had a fingerprint already, untouched
    int Failed,          // attempted but produced no fingerprint
    bool FpcalcAvailable // false if the run determined fpcalc to be unconfigured/missing
);

public class FingerprintService
{
    public FingerprintService(MusicBrainzService musicBrainzService);

    public Task<FingerprintBatchResult> PrecomputeFingerprintsAsync(
        IList<TrackInfo> tracks,
        IProgress<(int current, int total, string message)>? progress = null,
        Func<TrackInfo, Task>? onTrackComplete = null);

    public static void WriteFingerprintToTrackFile(TrackInfo track);
}
```

- The service stamps `track.AcoustIdFingerprint` in memory; **disk writing is caller-responsibility** so the import path can rely on its existing `WriteMetadataWithRetry` full-tag pass while the Edit Metadata path can do a focused per-field write.
- `onTrackComplete` is invoked exactly once per *newly computed* fingerprint (not for skipped-existing or failed tracks). The Edit Metadata caller passes a callback that wraps `FingerprintService.WriteFingerprintToTrackFile` in `Task.Run` so the synchronous TagLib save runs off the UI thread.
- On the first fpcalc-unavailable failure, the service sets a sticky flag so subsequent tracks skip the `GetFingerprintAsync` attempt and fail fast.

### 10.3 `LibraryImportService` refactor

`LibraryImportService.PrecomputeFingerprints` now delegates to `FingerprintService.PrecomputeFingerprintsAsync` and returns `result.Failed` to preserve the existing `int fingerprintFailures` contract. The import path passes neither `onTrackComplete` (relies on `WriteMetadataWithRetry` for tag writes) nor a different progress reporter. **Behavior of the import path is unchanged** — verified by the existing `LibraryImportServiceFingerprintTests` and `MetadataServiceFingerprintTests` regression suites.

### 10.4 Edit Metadata sidebar button

[Views/EditMetadataView.xaml](../Views/EditMetadataView.xaml) places a compact button immediately below `FingerprintSummaryText`:

```xml
<Button x:Name="FingerprintButton"
        Content="🎵 Fingerprint"
        Width="170" Height="24" Margin="0,4,0,0"
        FontSize="12" Background="#3C3C3C" Foreground="#CCCCCC" BorderBrush="#555555"
        Padding="6,2" Cursor="Hand" HorizontalAlignment="Center"
        Click="FingerprintButton_Click"
        ToolTip="Compute Chromaprint fingerprints for tracks that don't have one"/>
```

Styling matches the sidebar's secondary-button idiom (170px wide, 12pt, muted palette).

### 10.5 Click handler

`FingerprintButton_Click` ([Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs)):

1. Disables the button, shows the `ProgressBar` in determinate mode (`Maximum = _tracks.Count`).
2. Constructs `MusicBrainzService` per-click (mirrors `MusicBrainzButton_Click`'s pattern) and wraps it in a fresh `FingerprintService`.
3. Builds an `IProgress` reporter via `new Progress<...>(...)`. Because `Progress<T>` captures the construction-time `SynchronizationContext`, callbacks fire on the UI thread. The lambda updates `StatusTextBlock.Text`, advances `ProgressBar.Value`, and calls `UpdateFingerprintSummary()` — all UI-safe.
4. Awaits `PrecomputeFingerprintsAsync` inside `Task.Run` so the entire batch runs off the UI thread. The `onTrackComplete` callback re-wraps `FingerprintService.WriteFingerprintToTrackFile` in `Task.Run` so the synchronous TagLib save also runs on the thread pool.
5. After completion, sets a final status message:
   - `attempted == 0` (all skipped existing) → `"All tracks already fingerprinted"`
   - `!FpcalcAvailable` → `"fpcalc not configured — open Settings to configure"`
   - `Failed == 0` → `"Fingerprinted N tracks"`
   - else → `"Fingerprinted N tracks (K failed)"`
6. Re-enables the button, hides the ProgressBar, runs a final `UpdateFingerprintSummary()` to backstop the post-loop tick.

### 10.6 `UpdateFingerprintSummary` extraction

The fingerprint-summary block (lines counting fingerprinted tracks and toggling between "—", "No fingerprints", "N / M tracks fingerprinted", "All tracks fingerprinted") is extracted from `RefreshUI` into a standalone `UpdateFingerprintSummary()` method. `RefreshUI` calls it, and the click handler's `Progress` lambda calls it on each progress report.

Calling the full `RefreshUI` 30 times during a batch would re-decode the album artwork bitmap and re-run `ShowLookupService.GetSetlist` 30 times — extracting the summary update is a correctness fix, not a refactor for its own sake.

### 10.7 Disk-write helper

`FingerprintService.WriteFingerprintToTrackFile(TrackInfo track)` (public static) opens the file with TagLib, writes `ACOUSTID_FINGERPRINT` (FLAC Xiph) or the `Acoustid Fingerprint` TXXX frame (MP3), and saves. No-op when `track.AcoustIdFingerprint` is null/empty or the file is missing. Per-track exceptions are swallowed and logged to `Debug.WriteLine` — same pattern as the existing `WriteMbidToTracks` in Edit Metadata.

The helper lives on `FingerprintService` (not on `EditMetadataView`) so it remains unit-testable without WPF runtime initialization.

### 10.8 Tests

[DeadEditor.Tests/FingerprintServiceTests.cs](../DeadEditor.Tests/FingerprintServiceTests.cs) covers:

| Test | Verifies |
|---|---|
| `PrecomputeFingerprintsAsync_AllTracksAlreadyFingerprinted_SkipsAll` | Pre-stamped tracks → `SkippedExisting=N, Computed=0, Failed=0`; `onTrackComplete` not invoked |
| `PrecomputeFingerprintsAsync_FpcalcNotConfigured_FailsAttempted_SetsFpcalcAvailableFalse` | Offline `MusicBrainzService` → all attempted tracks counted as failed; `FpcalcAvailable=false`; pre-stamped tracks still counted as `SkippedExisting` |
| `PrecomputeFingerprintsAsync_ReportsProgress` | Progress reporter receives the per-track tuple format and the fpcalc-unavailable status message |
| `PrecomputeFingerprintsAsync_EmptyTrackList_ReturnsZeroCounts` | Empty input is well-defined; `FpcalcAvailable=true` (no determination made) |
| `WriteFingerprintToTrackFile_WithFingerprint_WritesToFlacXiph` | Writes the value to `ACOUSTID_FINGERPRINT` |
| `WriteFingerprintToTrackFile_WithEmptyFingerprint_PreservesExistingTag` | No-op when `AcoustIdFingerprint` is null/empty |
| `WriteFingerprintToTrackFile_OverwritesExistingValue` | Replaces an existing tag value |

The "successful fingerprinting" path is not unit-tested — it requires real audio plus `fpcalc.exe` (the same gap that exists in `LibraryImportServiceFingerprintTests`).
