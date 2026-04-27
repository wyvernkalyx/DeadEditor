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
- Surfacing fingerprint values in Track Info dialog or Edit Metadata view
