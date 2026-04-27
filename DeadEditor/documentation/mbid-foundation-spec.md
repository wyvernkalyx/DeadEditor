# MBID Foundation Phase 1: Read Path + Migration Tool

## 1. Goals

- Library scanner reads MBID from audio file tags and exposes it on `LibraryShow`
- Track Info dialog shows MBID under File Metadata section
- GUI migration tool walks the library, looks up each album on MusicBrainz, presents candidates for user confirmation, writes MBID to every track
- Migration is resumable and supports dry-run mode

## 2. Non-Goals (Phase 2+ work)

- Import-time write path (automatic MBID tagging during new imports)
- Releases owned/missing UI (depends on Phase 1 landing first)
- MusicBrainz release metadata beyond the ID itself (title, date, labels, etc.)
- Track-level MBIDs (recording MBID, track MBID)
- Enriching `releases.json` with MBIDs (separate future effort)
- MBID edit field in Edit Metadata view

## 3. Fixed Design Decisions

| # | Decision | Resolution |
|---|----------|------------|
| 1 | User-Agent string | `DeadEditor/1.0 (gregg.westgate@gmail.com)` — update in [MusicBrainzService.cs:23](Services/MusicBrainzService.cs#L23) |
| 2 | AcoustID API key | Leave hardcoded as `asa4wLQhwJ` in [ImportView.xaml.cs:84](Views/ImportView.xaml.cs#L84) (personal desktop tool, no quota concerns) |
| 3 | Dead code reuse | Reuse `SearchReleasesByNameAsync` ([MusicBrainzService.cs:109](Services/MusicBrainzService.cs#L109)) as fallback when fingerprinting fails or is unavailable. Do not rewrite. |
| 4 | Tag naming | Standard Picard convention — `MUSICBRAINZ_ALBUMID` for FLAC Vorbis comments, `MusicBrainz Album Id` (description) for MP3 ID3 TXXX frames. Match the existing 5-custom-field pattern. Do NOT use TagLib#'s `Tag.MusicBrainzReleaseId` shortcut. |
| 5 | Multi-disc | Write MBID to every track in every disc folder for the album. Same MBID across all tracks. |
| 6 | Already-tagged albums | Read path picks up existing MBIDs unchanged. Migration tool detects and skips already-tagged albums. Final summary reports "X need migration, Y already tagged, Z failed." |
| 7 | Rate limits | 350ms delay between AcoustID calls (conservative margin under 3/sec limit). 1100ms delay between MusicBrainz API calls (conservative margin under 1/sec limit). |
| 8 | Phase scope | Phase 1 = read path + migration tool. Import-time write path is **out of scope** (Phase 2). |
| 9 | Multi-band | Migration tool uses `LibrarySettings.PrimaryArtistName` ([LibrarySettings.cs:10](Models/LibrarySettings.cs#L10)) for MB artist filtering. Do not hardcode "Grateful Dead". |
| 10 | Migration resumability | State file persisted atomically (temp + rename pattern). Location: `%APPDATA%/DeadEditor/mbid-migration-state.json` (per existing user-data conventions — same directory as `settings.json`). |
| 11 | Dry-run | Toggle at start of run. When enabled, no tag writes happen; UI shows what would have been written. |

## 4. Tag Schema

### FLAC (Vorbis comments)

- **Field name:** `MUSICBRAINZ_ALBUMID`
- **Value:** Release MBID UUID as lowercase string (e.g., `"a1b2c3d4-e5f6-7890-abcd-ef1234567890"`)
- **Read via:** `xiph.GetFirstField("MUSICBRAINZ_ALBUMID")` — matching existing pattern at [LibraryGridView.xaml.cs:767-770](Views/LibraryGridView.xaml.cs#L767-L770)
- **Write via:** `xiph.SetField("MUSICBRAINZ_ALBUMID", mbid)` — matching existing pattern at [MetadataService.cs:409-413](Services/MetadataService.cs#L409-L413)

### MP3 (ID3v2 TXXX)

- **Frame description:** `MusicBrainz Album Id`
- **Value:** Release MBID UUID as lowercase string
- **Read via:** `UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", false)` — matching existing pattern at [LibraryGridView.xaml.cs:779-789](Views/LibraryGridView.xaml.cs#L779-L789)
- **Write via:** `SetId3v2TextField(id3v2, "MusicBrainz Album Id", mbid)` — matching existing pattern at [MetadataService.cs:456-466](Services/MetadataService.cs#L456-L466)

### Read scope

- Read is per-track (every track in an album should have the same MBID)
- `LibraryShow.MusicBrainzReleaseId` is populated from the first track's tag during library scan
- If tracks within an album disagree, use the first track's value (the migration tool writes the same MBID to all tracks, so disagreement indicates external tooling or partial tagging — not worth the scan cost of reading every file)

## 5. Read Path Implementation

### LibraryShow model

Add one property to [Models/LibraryShow.cs](Models/LibraryShow.cs) (after `Edition` at line 58):

```csharp
public string? MusicBrainzReleaseId { get; set; }
```

### Library scanner

Read the MBID tag during album scan in [LibraryGridView.xaml.cs](Views/LibraryGridView.xaml.cs), alongside the existing 5 custom fields.

**FLAC branch** (after line 770, inside the `xiph != null` block):
```csharp
var mbid = xiph.GetFirstField("MUSICBRAINZ_ALBUMID");
```

**MP3 branch** (after line 789, inside the `id3v2 != null` block):
```csharp
var mbidFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", false);
var mbid = mbidFrame?.Text.Length > 0 ? mbidFrame.Text[0] : null;
```

Then, after the existing tag assignments (around line 793):
```csharp
if (!string.IsNullOrEmpty(mbid))
    show.MusicBrainzReleaseId = mbid;
```

No new I/O — reads from the same `TagLib.File` instance already opened for the other custom fields.

### Track Info dialog

Add MBID row to [TrackInfoDialog.xaml.cs](TrackInfoDialog.xaml.cs), in both format branches:

**FLAC** (after line 64, inside the `xiph != null` block):
```csharp
AddField("Release MBID", xiph.GetFirstField("MUSICBRAINZ_ALBUMID") ?? "");
```

**MP3** (after line 77, inside the `id3v2 != null` block):
```csharp
AddField("Release MBID", GetId3v2TextField(id3v2, "MusicBrainz Album Id"));
```

The existing `AddField()` method handles display, em-dash for empty values, and the copy-to-clipboard button. No layout changes needed.

## 6. Migration Tool UI

### Location

New button in Settings view under the **Library Maintenance** section ([SettingsView.xaml:130-141](Views/SettingsView.xaml#L130-L141)), alongside the existing "Re-enrich Library from shows.json" button. The migration tool itself is a new view that replaces the main content area via shell navigation (same pattern as all other views).

**Judgment call:** I placed the migration button in Settings > Library Maintenance rather than as a new sidebar item because: (a) migration is a one-time/occasional maintenance task, not a primary workflow; (b) it's adjacent to the existing "Re-enrich" tool which is conceptually similar; (c) adding a sidebar item for a transient tool would clutter the permanent navigation. The migration button navigates to a full MbidMigrationView via the shell, not an inline settings panel.

### Layout

The migration view has four states, shown sequentially:

#### 6.1 Start screen

Displayed when navigating to MbidMigrationView.

- **Title:** "MusicBrainz Release ID Migration"
- **Counts panel:**
  - Total albums in library: `{N}`
  - Already tagged (will be skipped): `{Y}`
  - Needing migration: `{N - Y}`
- **Dry-run toggle:** CheckBox, label "Dry run (preview only — no tags will be modified)", default OFF
- **"Start Migration" button** — disabled if all albums already tagged
- **Resume banner** (visible only if state file exists with incomplete migration): "Previous migration in progress — {X} of {Y} complete. [Resume] [Start Fresh]"
  - "Resume" continues from where it left off
  - "Start Fresh" deletes the state file and starts over (confirm dialog first)

Counts are calculated by scanning all `LibraryShow` entries from the library grid. Already-tagged detection reads the `MusicBrainzReleaseId` property (populated by the read path).

#### 6.2 Progress screen (during migration)

- **Current album:** Album name and folder path
- **Progress bar:** `{completed} of {total}` with percentage
- **Running totals:**
  - Matched: `{n}` (MBID confirmed and written/recorded)
  - Needs review: `{n}` (queued for candidate dialog — displayed during batch processing)
  - Skipped: `{n}` (user chose "skip" or already tagged)
  - Failed: `{n}` (lookup errors)
- **"Pause" button** — pauses after current album finishes processing; state is saved
- **"Cancel" button** — same as pause but also navigates back to start screen

Processing order: albums are processed sequentially. For each album, the tool:
1. Checks if already tagged → skip
2. Checks state file for previous result → skip if complete
3. Runs fingerprint lookup (AcoustID → MusicBrainz)
4. If no fingerprint results, falls back to name search
5. If candidates found → shows candidate review dialog (Section 6.3)
6. If no candidates → records as "no match" with manual entry option

#### 6.3 Candidate review dialog (interactive, per ambiguous album)

Modal dialog overlaying the progress screen. Blocks progress until resolved.

**Current album info panel:**
- Folder name (from `FolderPaths[0]`)
- Album name (from tags or folder)
- Track count
- Date (if available)
- Artist (from tags)

**Candidate list** (from fingerprint + name search, deduplicated by release MBID):

Each candidate shows:
- Release title
- Artist
- Date (first release date)
- Country / label
- Catalog number (if available)
- Format / disc count / track count
- Release MBID (copyable)
- "View on MusicBrainz" link → opens `https://musicbrainz.org/release/{mbid}` in default browser

**Selection:** Radio buttons — select one candidate, or choose "Skip this album"

**Manual entry field:** Text box at bottom of candidate list, labeled "Or enter MBID manually:" — accepts a UUID string, validated as a valid GUID before enabling Confirm

**Buttons:**
- "Confirm" — writes MBID to all tracks (or records choice if dry-run), advances to next album
- "Skip" — records album as skipped in state file, advances to next album

#### 6.4 Completion screen

- **Title:** "Migration Complete" (or "Dry Run Complete" if dry-run)
- **Summary counts:**
  - Matched and tagged: `{n}`
  - Skipped by user: `{n}`
  - Already tagged (pre-existing): `{n}`
  - Failed (no candidates): `{n}`
  - Total: `{n}`
- **Failures list:** Expandable section listing each failed album with folder path and reason (e.g., "No fingerprint results and no name matches", "fpcalc error: file not found", "Network error")
- **Dry-run banner** (if applicable): "DRY RUN — no tags were modified. Run again with dry-run disabled to apply changes."
- **"Close" button** — navigates back to Settings

### Resumability

**State file:** `%APPDATA%\DeadEditor\mbid-migration-state.json`

**Structure:**
```json
{
  "startedAt": "2026-04-19T14:30:00Z",
  "dryRun": false,
  "albums": {
    "D:\\Music\\Grateful Dead\\1977-05-08 Barton Hall": {
      "status": "matched",
      "mbid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "completedAt": "2026-04-19T14:31:05Z"
    },
    "D:\\Music\\Grateful Dead\\American Beauty (1970)": {
      "status": "skipped",
      "completedAt": "2026-04-19T14:31:10Z"
    },
    "D:\\Music\\Grateful Dead\\1972-04-08 Wembley": {
      "status": "failed",
      "reason": "No candidates found",
      "completedAt": "2026-04-19T14:31:15Z"
    }
  }
}
```

- **Atomic write** after each album completes — temp file + rename pattern matching [NormalizationService.cs:625-630](Services/NormalizationService.cs#L625-L630)
- On resume of a **real run**: albums with any status in the state file are skipped (already completed)
- On resume of a **real run after a prior dry-run**: albums are re-processed but the candidate dialog **pre-selects** the choice made during dry-run (see Dry-run behavior below)
- Missing folders (deleted between runs): detected during scan, logged as warning, state entry left as-is

### Dry-run behavior

- The candidate dialog and confirm flow work identically to a real run
- The "Confirm" button in candidate dialog records the choice in state (with `"dryRun": true` flag on the state file) but does NOT write tags
- Summary screen shows "DRY RUN — no tags were modified"

**Dry-run → real-run transition:**

When the user ran a dry-run previously and now starts a real run:
- The state file's `dryRun` flag is `true`, indicating all entries are informational only
- All dry-run entries are re-processed — they are NOT skipped
- The candidate review dialog still appears for every album that needs migration
- The radio button for the candidate the user selected during dry-run is **pre-selected** (matched by MBID from the dry-run state entry)
- The user can click Confirm immediately to accept, OR change the selection before confirming
- This preserves the safety of explicit confirmation while removing the friction of re-reviewing from scratch

When the user ran a real run previously and resumes after interruption:
- Already-completed albums (status `"matched"`, `"skipped"`, or `"failed"` in a non-dry-run state file) are skipped entirely — no dialog shown
- The migration picks up from the first album without a state entry

## 7. Architecture

### New/changed files

| File | Status | Change |
|---|---|---|
| [Models/LibraryShow.cs](Models/LibraryShow.cs) | **Modified** | Add `MusicBrainzReleaseId` property (1 line) |
| [Views/LibraryGridView.xaml.cs](Views/LibraryGridView.xaml.cs) | **Modified** | Read `MUSICBRAINZ_ALBUMID` / `MusicBrainz Album Id` during library scan (~6 lines, follows existing pattern at lines 764-789) |
| [TrackInfoDialog.xaml.cs](TrackInfoDialog.xaml.cs) | **Modified** | Add "Release MBID" row to File Metadata section (~2 lines, after lines 64 and 77) |
| [Services/MusicBrainzService.cs](Services/MusicBrainzService.cs) | **Modified** | Update User-Agent (line 23); add centralized rate limiting timestamps |
| [Views/SettingsView.xaml](Views/SettingsView.xaml) | **Modified** | Add "MBID Migration" button in Library Maintenance section (after line 139) |
| [Views/SettingsView.xaml.cs](Views/SettingsView.xaml.cs) | **Modified** | Add click handler for migration button → shell navigation |
| `Views/MbidMigrationView.xaml` + `.cs` | **NEW** | Migration tool UI (start, progress, completion screens) |
| `Views/MbidCandidateDialog.xaml` + `.cs` | **NEW** | Candidate picker modal dialog |
| `Services/MbidMigrationService.cs` | **NEW** | Orchestrates migration: iterates albums, manages state file, coordinates lookups and writes |

### Existing code to reuse

| What | Where | How it's used |
|---|---|---|
| `MusicBrainzService.LookupAllReleasesAsync()` | [MusicBrainzService.cs:200](Services/MusicBrainzService.cs#L200) | Primary fingerprint-based match path (already does multi-track fingerprint → release candidates) |
| `MusicBrainzService.SearchReleasesByNameAsync()` | [MusicBrainzService.cs:109](Services/MusicBrainzService.cs#L109) | Name-based fallback search (currently dead code — will be called by migration service) |
| `ReleaseOption` model | [ReleaseSelectorDialog.xaml.cs:55-67](ReleaseSelectorDialog.xaml.cs#L55-L67) | Candidate data structure — reused as-is in the candidate dialog |
| Xiph read/write pattern | [MetadataService.cs:406-413](Services/MetadataService.cs#L406-L413) | FLAC tag write pattern for MBID |
| TXXX read/write pattern | [MetadataService.cs:456-466](Services/MetadataService.cs#L456-L466) | MP3 tag write pattern for MBID |
| Atomic write pattern | [NormalizationService.cs:625-630](Services/NormalizationService.cs#L625-L630) | Temp file + delete + rename for state file persistence |
| `LibrarySettings.PrimaryArtistName` | [LibrarySettings.cs:10](Models/LibrarySettings.cs#L10) | Artist filtering for MB name search fallback |

### MbidMigrationService responsibilities

- Accept a list of `LibraryShow` entries from the caller (MbidMigrationView passes them)
- Iterate albums, skipping those already in state file or already tagged
- For each album:
  1. Attempt fingerprint lookup via `MusicBrainzService.LookupAllReleasesAsync()`
  2. If no results, attempt name search via `SearchReleasesByNameAsync()` using `AlbumName` + `PrimaryArtistName`
  3. Deduplicate candidates by `ReleaseId`
  4. Raise event or callback for UI to show candidate dialog
  5. Receive user's selection (MBID, skip, or cancel)
  6. If confirmed and not dry-run: write MBID to all tracks in all `FolderPaths`
  7. Update state file atomically
- Expose progress events for UI binding (current album, counts, completion)

### MBID tag write implementation

New private method in `MbidMigrationService`:

```csharp
private void WriteMbidToAlbum(LibraryShow show, string mbid)
{
    foreach (var folder in show.FolderPaths)
    {
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
                    SetId3v2TextField(id3v2, "MusicBrainz Album Id", mbid);
            }
            file.Save();
        }
    }
}
```

Pattern follows [LibraryImportService.cs:275-300](Services/LibraryImportService.cs#L275-L300) (custom field writes during import).

## 8. API Usage

### User-Agent

Update [MusicBrainzService.cs:23](Services/MusicBrainzService.cs#L23):

```csharp
// Before:
_httpClient.DefaultRequestHeaders.Add("User-Agent", "DeadEditor/1.0 (https://github.com/yourrepo)");

// After:
_httpClient.DefaultRequestHeaders.Add("User-Agent", "DeadEditor/1.0 (gregg.westgate@gmail.com)");
```

### Rate limiting

Implement centralized rate limiting in `MusicBrainzService` (not scattered at call sites):

```csharp
private DateTime _lastMbCall = DateTime.MinValue;
private DateTime _lastAcoustIdCall = DateTime.MinValue;

private async Task ThrottleMusicBrainz()
{
    var elapsed = (DateTime.UtcNow - _lastMbCall).TotalMilliseconds;
    if (elapsed < 1100)
        await Task.Delay((int)(1100 - elapsed));
    _lastMbCall = DateTime.UtcNow;
}

private async Task ThrottleAcoustId()
{
    var elapsed = (DateTime.UtcNow - _lastAcoustIdCall).TotalMilliseconds;
    if (elapsed < 350)
        await Task.Delay((int)(350 - elapsed));
    _lastAcoustIdCall = DateTime.UtcNow;
}
```

- Call `ThrottleMusicBrainz()` before every MB API request (replaces the existing `await Task.Delay(1000)` calls at lines 175, 432, 476)
- Call `ThrottleAcoustId()` before every AcoustID API request
- Applies to all callers including the existing import flow — acts as a safety net, does not break current behavior

### Error handling

| Scenario | Handling |
|---|---|
| HTTP 503 (rate limit) | Wait 5 seconds, retry once. If 503 again, record as failed and move on. |
| HTTP 404 (not found) | Record as "no match" and move on. |
| Network failure / timeout | Show retry/skip/cancel options in UI. User decides per-album. |
| fpcalc.exe missing or not configured | Skip fingerprinting entirely, go straight to name search fallback. Log a warning on the progress screen: "fpcalc not configured — using name search only." |
| fpcalc execution error (specific file) | Skip fingerprinting for this album, fall back to name search. |
| Invalid JSON response | Record as failed with error message, move on. |

## 9. Matching Strategy

Order of attempts per album:

1. **Already tagged** — `LibraryShow.MusicBrainzReleaseId` is not null/empty → skip, record as "already tagged"
2. **State file hit** — album folder path exists in state file with a completed status → skip if real-run state; if dry-run state and current run is real, re-process with pre-selected candidate from dry-run choice
3. **Fingerprint match** (AcoustID → MusicBrainz release) — most accurate path
   - Uses `LookupAllReleasesAsync()` which fingerprints up to 4 tracks per album
   - Requires fpcalc.exe to be configured and present
4. **Name search fallback** — if fingerprint finds nothing (or fpcalc unavailable), use `SearchReleasesByNameAsync(albumName, primaryArtistName)`
   - `albumName` comes from `LibraryShow.AlbumName` or `LibraryShow.OfficialRelease`
   - `primaryArtistName` comes from `LibrarySettings.PrimaryArtistName`
5. **Manual entry** — if no candidates from either path, the candidate dialog shows only the manual MBID entry field. User can paste a UUID (validated as GUID format) or skip.

**Always show candidates for user confirmation** — never auto-apply, even on high-confidence fingerprint matches with a single result. The user must explicitly confirm every MBID assignment.

### Candidate deduplication

Fingerprint and name search may return overlapping results. Before displaying:
- Deduplicate by `ReleaseOption.ReleaseId` (the MB release MBID)
- Sort by: exact title match first, then by date (newest first)

## 10. Edge Cases

| Scenario | Handling |
|---|---|
| Album with no decodable audio files | Name search only. If album has no name either, flag for manual entry. |
| fpcalc.exe missing | Skip fingerprinting for all albums, use name search only. Show persistent warning banner on progress screen. |
| MusicBrainz returns zero candidates | Show candidate dialog with only the manual entry field. User can paste MBID or skip. |
| User cancels migration mid-run | State file already persisted up to last completed album. On next launch, resume banner shown. |
| User pauses migration | Same as cancel but stays on migration view. Can resume immediately. |
| User deletes a library album between runs | On resume, detect that folder no longer exists. Mark state entry as "folder missing" (warning). Do not error — continue with remaining albums. |
| Dry-run followed by real run | State file's `dryRun` flag is checked. A real run re-processes all dry-run entries. Candidate dialogs pre-select the dry-run choice so the user can confirm quickly or revise. |
| Album with tracks in mixed formats (FLAC + MP3) | Write MBID using the appropriate method per file (Xiph for FLAC, TXXX for MP3). Both get the same MBID. |
| Multi-disc album (`FolderPaths` has multiple entries) | Write MBID to all tracks in all folders. Candidate lookup uses tracks from the first folder only (consistent with existing fingerprint behavior). |
| Some tracks already tagged with different MBID | Migration tool overwrites with the user-confirmed MBID. The user explicitly chose this release — their choice wins. |
| Concurrent access (user imports while migration runs) | Not supported. Migration should not run during active imports. The migration start screen should check for this and warn if ImportView is active. |

## 11. Testing Plan

### Automated tests (xUnit)

| Test | What it verifies |
|---|---|
| MBID tag read from FLAC | `GetFirstField("MUSICBRAINZ_ALBUMID")` returns correct value from a sample FLAC |
| MBID tag read from MP3 | TXXX frame `MusicBrainz Album Id` returns correct value from a sample MP3 |
| MBID tag write to FLAC | `SetField` writes, re-read returns same value |
| MBID tag write to MP3 | `SetId3v2TextField` writes, re-read returns same value |
| Already-tagged detection | `LibraryShow` with non-null `MusicBrainzReleaseId` is skipped |
| State file persist and resume | Write state, reload, verify processed albums are skipped |
| State file atomic write | Verify temp + rename pattern (file exists after write, no partial writes) |
| Dry-run state pre-selects in real run | Dry-run state entries are re-processed in a real run, with candidate dialog pre-selecting the dry-run choice |
| MBID UUID validation | Valid GUIDs accepted, non-GUID strings rejected |

### Manual verification steps (for Gregg)

1. **Read path:** Import or locate an album already tagged with `MUSICBRAINZ_ALBUMID` (e.g., from Picard). Open Track Info — confirm "Release MBID" row shows the value. Confirm it appears in the library grid's `LibraryShow` object (verify via debugger or add temporary display).

2. **Migration start screen:** Navigate to Settings → Library Maintenance → "MBID Migration". Confirm album counts are correct (total vs. already tagged).

3. **Dry-run (5 albums):** Enable dry-run checkbox. Start migration. Process 5 albums through candidate review. Confirm that after completion, Track Info for those albums still shows no MBID. Confirm state file exists at `%APPDATA%/DeadEditor/mbid-migration-state.json`.

4. **Real run with dry-run pre-select (5 albums):** Disable dry-run. Start migration. Process same 5 albums (they should re-appear since dry-run state entries are re-processed). Confirm that the candidate dialog pre-selects the choice made during dry-run. Confirm candidates (accept or change selection), write MBIDs. After completion, open Track Info for each — confirm "Release MBID" shows the chosen MBID.

5. **Resume:** Start migration on remaining library. After ~10 albums, click Pause. Close and reopen the app. Go to migration screen — confirm resume banner appears with correct count. Resume and verify it skips already-completed albums.

6. **Fallback:** Temporarily clear fpcalc path in Settings. Run migration on a known album. Confirm it falls back to name search (warning banner visible). Confirm candidates still appear.

7. **Manual entry:** Find an album that returns no candidates. Confirm the manual MBID entry field appears. Paste a valid MBID from musicbrainz.org. Confirm it's written correctly.

8. **App restart persistence:** After tagging several albums, restart DeadEditor. Open library — confirm `MusicBrainzReleaseId` is populated on `LibraryShow` for tagged albums (visible in Track Info).

## 12. Out of Scope / Future Work

- ~~**Phase 2:** Import-time MBID write — persist `ReleaseOption.ReleaseId` from existing import flow into tags during import~~ — **Completed (Commit 1).** `AlbumInfo.MusicBrainzReleaseId` is set in [ImportView.ApplyMusicBrainzData](Views/ImportView.xaml.cs#L1309) from the user's `ReleaseSelectorDialog` choice and written to FLAC `MUSICBRAINZ_ALBUMID` and MP3 `MusicBrainz Album Id` TXXX in [LibraryImportService.WriteMetadataWithRetry](Services/LibraryImportService.cs#L228). The "Write Metadata" in-place path via `MetadataService.WriteMetadata` is still pending (Commit 1.6).
- ~~**Commit 1.5:** Import-time MBID-driven re-match — when source files already carry `MUSICBRAINZ_ALBUMID`, the 🔎 button uses the existing MBID directly via `MusicBrainzService.GetReleaseTracksAsync` and bypasses fingerprinting; a "Re-fingerprint" checkbox forces the fingerprint path on demand.~~ — **Completed (Commit 1.5).** Per-track MBID values are read on-demand via [MbidModalHelper.ReadMbidFromFile](Services/MbidModalHelper.cs) using `TagLib.ReadStyle.PictureLazy`, then aggregated by [MbidModalHelper.ComputeModal](Services/MbidModalHelper.cs). When tracks disagree, the most common value is used and a "⚠ Tracks have inconsistent MBIDs" warning is shown in the status bar. The candidate dialog (`ReleaseSelectorDialog`) is shown for both paths so the user always confirms before tags are written. The checkbox is session-scoped only — it does not mutate source files.
- **Phase 2:** MBID edit/clear field in Edit Metadata view
- **Phase 2:** Enrich `releases.json` entries with MBIDs (enables releases owned/missing UI)
- **Phase 3:** Recording-level MBIDs (per-track `MUSICBRAINZ_TRACKID`)
- **Phase 3:** Releases owned/missing UI (requires MBIDs populated across library + enriched releases.json)
