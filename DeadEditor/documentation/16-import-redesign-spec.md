# 16 — Import Screen Redesign Spec

**Status:** Mostly shipped. Core redesign is live in Views/ImportView.xaml (the file replaced the original MainWindow plan). Date auto-append rule revised in this commit; code consolidation onto the new rule is the next commit. Remaining outstanding: editable Folder Name Preview + reset (↻) button + FolderNameOverride/CustomFolderName — not yet built.

> **MusicBrainz removed (2026-06).** The 🔎 MusicBrainz lookup button, its
> Re-fingerprint checkbox, the fingerprint/MBID lookup, and the release-selector
> apply path were removed from ImportView as part of the MusicBrainz-removal arc.
> The action bar no longer carries a MusicBrainz button. The Enrich stepper stage
> now reflects only **pre-existing** `MUSICBRAINZ_ALBUMID` tags found in source
> files at folder-load (a pure tag scan) — there is no longer any in-app way to
> acquire an MBID from the import view. The MusicBrainz sections below are retained
> as the historical redesign record; full prose reconciliation lands with the doc
> sweep at the end of the removal arc.

## Overview

Complete redesign of MainWindow (the import/metadata editor screen) to replace the current side-panel layout with a streamlined, dBpowerAmp-inspired interface. The goal is a single-screen workflow where what you see is what gets written to files.

**Core Principles:**
- One editable Title column — no Song vs Preview split
- Album info as a horizontal bar, not a side panel
- No album type radio buttons — dropdown tucked in artwork panel, defaults to Auto-detect
- MusicBrainz is a populate action, not a separate workflow
- Inline editing everywhere — double-click to edit titles and dates
- Date inheritance — tracks inherit album date unless overridden
- Date auto-appends to title — every track carries its most-specific available date (TrackDate → AlbumDate → Year)

---

## Layout Structure

The window is organized top-to-bottom:

```
┌─────────────────────────────────────────────────────────────┐
│ [Select Folder…]  [folder path display]                     │  ← Folder Bar
├─────────────────────────────────────────────────────────────┤
│ [Read] [✨ Normalize] [Renumber] │ [View Info] │ status │ [Write] [Import] [Cancel] │  ← Action Bar  (MusicBrainz button removed)
├───────────────────────────────────────────────┬─────────────┤
│ Artist | Date | Venue | City,ST | Album | Year│             │  ← Album Info Bar
├───────────────────────────────────────────────┤  Artwork    │
│ # │ Title (editable)       │ Segue │ Date │ Time │  Panel    │  ← Track List
│ 1 │ Bertha                 │  [ ]  │ gray │ 5:42 │           │
│ 2 │ Mama Tried             │  [ ]  │ gray │ 2:42 │  [img]    │
│ ...                                              │  Change   │
│ 1 │ Good Lovin' (1971-07-02) │ [ ] │ white│17:47 │  Remove   │
│ ...                                              │           │
│                                                  │  Type: v  │
│                                                  │  Preview  │
│                                                  │  Path     │
├───────────────────────────────────────────────┴─────────────┤
│ [⏮] [▶] [⏹] [⏭]  Track Name  ──●────── 1:24 / 3:35  🔊━━ │  ← Playback Bar
└─────────────────────────────────────────────────────────────┘
```

---

## Section 1: Folder Bar

**Layout:** Horizontal bar, full width.

| Element | Type | Behavior |
|---------|------|----------|
| Select Folder… | Button | Opens folder browser dialog. On selection, triggers Read from Files automatically |
| Folder Path | Read-only text | Shows full path to selected folder. Monospace font. Truncates with ellipsis |

**No changes from current behavior** except visual layout (moves from top of side panel to full-width bar).

---

## Section 2: Action Bar

**Layout:** Horizontal bar with action buttons left, status center, import button right.

| Element | Type | Behavior |
|---------|------|----------|
| Read from Files | Button | Reads ID3 tags from audio files, populates Title column and album fields |
| 🔎 MusicBrainz Lookup | Button (accent) | Runs fingerprint lookup → release selector → populates titles and album fields |
| ✨ Normalize All | Button (accent) | Normalizes all titles: fuzzy match to song database, fix dates to yyyy-MM-dd, strip venue info |
| Renumber Tracks | Button | Renumbers tracks sequentially |
| View Info File | Button | Opens info file viewer if .txt file exists in folder |
| Status text | Label | Shows "Matched X of Y songs" — updates after normalize |
| Import to Library | Button (primary) | Copies files to managed library and writes metadata to the copies (source files never written) |
| Cancel | Button | Closes window, stops any playback |

**Key change:** MusicBrainz is now just a button in the action bar. When clicked:
1. Fingerprints first 3 tracks (existing logic)
2. Shows release selector if multiple results
3. On selection, fetches track data via `/ws/2/release/{id}?inc=recordings+artists`
4. Populates album fields (Artist, Album Name, Year) and overwrites Title column with MusicBrainz track titles
5. User then clicks Normalize to clean up the titles

**No confirmation dialog needed.** The user can see exactly what changed in the Title column and undo by clicking Read from Files to reload original metadata.

---

## Section 3: Album Info Bar

**Layout:** Horizontal grid of labeled input fields, full width above the track list.

| Field | Label | Placeholder | Notes |
|-------|-------|-------------|-------|
| Artist | ARTIST | (empty) | Applied to all tracks. Auto-filled from file metadata or MusicBrainz |
| Date | ALBUM DATE | yyyy-MM-dd | The primary date for this album/concert. Tracks inherit this unless overridden |
| Venue | VENUE | Venue name | Concert venue. May be empty for official releases |
| City, State | CITY, STATE | City, ST | Location. May be empty for official releases |
| Album / Release Name | ALBUM / RELEASE NAME | Optional | Album title or official release name |
| Collection Name | COLLECTION NAME | Optional | Collection/box set name (applies to either type) |
| Year | YEAR | Release year | Release year (may differ from performance date) |

**All fields are always visible and always editable.** No conditional show/hide based on album type. The user fills in what's relevant and leaves the rest blank.

**Auto-population from folder name:** When a folder is selected, parse the folder name for date, venue, city, state using existing regex patterns. Populate fields but allow user override.

---

## Section 4: Track List

**Layout:** Full-width data grid with 5 columns.

### Columns

| Column | Width | Editable | Content |
|--------|-------|----------|---------|
| # | 40px | No | Track number, right-aligned, monospace |
| Title | Flex (fills remaining) | Yes (double-click) | Song title — THIS IS WHAT GETS WRITTEN TO FILES |
| Segue | 50px | Yes (checkbox) | Checkbox, checked = segue into next track |
| Date | 110px | Yes (double-click) | Performance date for this track |
| Time | 60px | No | Track duration, right-aligned, monospace |

### Title Column Behavior

- **What you see is what gets written.** The Title column is the final metadata value.
- **Double-click to edit.** Opens inline text input. Enter to confirm, Escape to cancel.
- **Unmatched songs shown in gold (#D7BA7D).** After normalization, songs not found in the database are highlighted so the user knows they need attention.
- **Edits are to the song name portion only.** When editing inline, the date suffix `(yyyy-MM-dd)` is stripped from the edit field and re-appended automatically after editing based on the track's date.

### Date Column Behavior

- **Empty = inherits album date.** Displayed in gray/dim text with italic style.
- **Filled = track-specific override.** Displayed in white/bright text, normal weight.
- **Double-click to edit.** Opens inline text input for date entry.
- **Clearing a track date** reverts it to inheriting the album date.

### Date-Title Auto-Append Rule

Every track title carries the most-specific available date as a suffix. The suffix is absent only when no date information of any kind is available.

The effective date is selected by the following preference order:

1. Track-specific `TrackDate`, if set and non-empty
2. Album `AlbumDate`, if set and non-empty
3. Album `Year`, if set and non-empty (typically studio albums where no `yyyy-MM-dd` exists)
4. None — suffix is omitted

Format: `Song Name (yyyy-MM-dd)` or `Song Name (yyyy)` for the year fallback. Segue marker, when present, comes before the date: `Song Name > (yyyy-MM-dd)`.

#### Examples

| Album date | Track date | Year | Segue | Title |
|---|---|---|---|---|
| empty | `1971-07-02` | — | no | `Good Lovin' (1971-07-02)` |
| `1971-07-02` | empty | — | no | `Good Lovin' (1971-07-02)` |
| `1971-04-28` | `1971-07-02` | — | no | `Good Lovin' (1971-07-02)` |
| `1971-07-02` | `1971-07-02` | — | no | `Good Lovin' (1971-07-02)` |
| `1971-07-02` | empty | — | yes | `Good Lovin' > (1971-07-02)` |
| empty | empty | `1973` | no | `Eyes of the World (1973)` |
| empty | empty | empty | no | `Eyes of the World` |

#### Rationale

Self-describing titles travel with the file. A track exported to a car stereo, a phone, or a playlist app shows when it was recorded regardless of whether the listening device displays the album context. The minor visual redundancy in the import grid (where the album date is already visible) is acceptable in exchange for tag completeness on the device.

This rule supersedes an earlier draft that suppressed the suffix when the effective track date equaled the album date. The suppression rule prioritized grid cleanliness over tag completeness; the trade is now in the opposite direction.

### Row Selection

- **Single click** selects a row (highlight with selection color).
- **Selected track** is what plays when Play is clicked.
- **Double-click** on Title or Date cell enters inline edit mode.

### Row Colors

- Alternating row backgrounds for readability (even rows slightly lighter).
- Hover highlight on all rows.
- Selected row has distinct background color.
- Gold text (#D7BA7D) for unmatched song titles.

---

## Section 5: Artwork Panel

**Layout:** Narrow right-side panel (200px wide), vertically stacked.

| Element | Type | Behavior |
|---------|------|----------|
| Artwork display | Image (176×176) | Shows album art. Placeholder if none |
| Change… | Button | Opens file picker for image |
| Remove | Button | Clears artwork |
| Album Type | Dropdown | Options: Auto-detect, Audience Recording, Official Release. Default: Auto-detect |
| Folder Name Preview | Label (gold text) | Shows computed folder name based on album info fields and type |
| Write Path | Label (gray text) | Shows full path where files will be written |

**Album Type dropdown** replaces the radio buttons. It's tucked in the artwork panel, out of the main workflow.

The combobox is the source of truth for `_albumInfo.Type` after load. Type is set once when the album info is first populated:
1. If the source folder has an `ALBUMTYPE` tag (FLAC Xiph or ID3v2 TXXX) → Type comes from the tag.
2. Otherwise → `AlbumTypeInference.Infer` runs once against the populated fields:
   - Album/Release Name filled, no Venue → Official Release
   - Date + Venue filled, no Album Name → Audience Recording
   - Date + Venue + Album Name → Official Release
   - Otherwise → Audience Recording (default)
3. The user may then change Type via the combobox at any time.

**Subsequent text edits to Album Info fields do NOT re-trigger inference.** Once the combobox shows a value (whether from tag, inference, or user selection), it persists until the user chooses something else. Prior behavior re-asserted Type on every keystroke based on `AlbumName` non-empty, silently overriding combobox selections — that auto-override has been removed.

**Folder Name Preview** shows the computed folder name based on album info fields and type. **This field is editable** - user can click to override the auto-computed name. When manually edited, auto-compute stops (override state). A small reset button (↻) next to the preview reverts to auto-computed mode. Editable text field styled like the preview (gold text, #D7BA7D).

**Folder Name Logic:**
- **Audience Recording:** `YYYY-MM-DD - Venue - City, ST` (if Collection Name: append `: Collection Name`)
- **Official Release with Date+Venue:** `YYYY-MM-DD - Venue - City, ST : Album Name` (if Collection Name: append `: Collection Name`)
- **Official Release without Date+Venue:** `Album Name (Year)` (if Collection Name: append `: Collection Name`)

**Write Path** shows the full destination path based on library settings and folder name. All types use the universal layout:
- `LibraryRootPath/{Artist}/[folder name]` (see [13-library-import-service.md](13-library-import-service.md) for the full naming rules)

---

## Section 6: Playback Bar

**Layout:** Horizontal bar at bottom. Already implemented in current version.

| Element | Type | Behavior |
|---------|------|----------|
| ⏮ Previous | Button | Previous track |
| ▶ Play | Button | Play selected track. Toggles to ⏸ Pause |
| ⏹ Stop | Button | Stop playback |
| ⏭ Next | Button | Next track |
| Track info | Label | Shows "# . Title" of currently playing track |
| Progress bar | Slider | Seek within track |
| Time display | Labels | Current time / total time, monospace |
| Volume | Slider | Volume control |

**No changes from current implementation** except visual consistency with new layout.

---

## Data Model Changes

### TrackInfo Model Updates

```csharp
public class TrackInfo
{
    // Existing properties
    public int TrackNumber { get; set; }
    public string Title { get; set; }           // Computed: SongName + optional (Date)
    public string SongName { get; set; }         // NEW: Just the song name without date
    public string TrackDate { get; set; }        // NEW: Track-specific date override (empty = inherit)
    public bool Segue { get; set; }
    public string Duration { get; set; }
    public string FilePath { get; set; }
    
    // Removed
    // public string PreviewMetadata { get; set; }  — no longer needed
    // public bool HasMusicBrainzData { get; set; }  — no longer needed
}
```

### AlbumInfo Model Updates

Remove album-type-specific field visibility logic. All fields always available:

```csharp
public enum AlbumType
{
    AudienceRecording,  // Audience/taper recordings (was Live)
    OfficialRelease     // Official releases (covers studio, live albums, box sets, series)
}

public class AlbumInfo
{
    public string Artist { get; set; }
    public string AlbumDate { get; set; }        // Primary date (yyyy-MM-dd)
    public string Venue { get; set; }
    public string CityState { get; set; }
    public string AlbumName { get; set; }         // Album or official release name
    public string CollectionName { get; set; }    // Collection/box set name (optional, applies to either type)
    public string Year { get; set; }              // Release year (string, may be empty)
    public AlbumType Type { get; set; }           // Inferred or user-selected
    public byte[] ArtworkData { get; set; }
    public string ArtworkMimeType { get; set; }
    public bool FolderNameOverride { get; set; }  // True if user manually edited folder name
    public string CustomFolderName { get; set; }  // User's custom folder name (when override=true)
}
```

**Backward Compatibility Mapping:**
When reading existing metadata from older imports:
- `Live` → `AudienceRecording`
- `Studio`, `OfficialRelease`, `BoxSet` → `OfficialRelease`

---

## Workflow: Typical Import Flow

### Audience Recording

1. User clicks **Select Folder** → selects `1977-05-08 Barton Hall, Cornell University, Ithaca, NY`
2. **Read from Files** runs automatically
3. Album fields auto-populate: Date=1977-05-08, Venue=Barton Hall, Cornell University, City,State=Ithaca, NY
4. Album Type set at load to Audience Recording (no ALBUMTYPE tag, inference matches Date+Venue+no AlbumName pattern)
5. Track titles populate from file metadata
6. User clicks **✨ Normalize All** → titles cleaned, matched to song database
7. User reviews, edits any unmatched (gold) titles manually
8. Folder Name Preview shows: `1977-05-08 - Barton Hall - Ithaca, NY`
9. User clicks **Import to Library**

### Official Release (Studio Album)

1. User selects folder `Grateful Dead - Skull and Roses (flac)`
2. Read from Files populates track titles from tags
3. User clicks **🔎 MusicBrainz Lookup** → finds "Skull & Roses", populates Album Name and Year
4. Album Type set to Official Release by the MusicBrainz lookup itself (a MB hit is by definition an official release)
5. Title column updates with MusicBrainz track names
6. User clicks **✨ Normalize All** → titles matched to song database
7. User sets bonus track dates by double-clicking Date column for disc 2 tracks
8. Date auto-appends to bonus track titles
9. Folder Name Preview shows: `Skull & Roses (1971)` (no date/venue)
10. User clicks **Import to Library**

### Official Release with Collection (Box Set Equivalent)

1. User selects folder for one concert within a collection
2. Read from Files populates
3. User fills in Date, Venue, City/State (live recording info)
4. User fills in Collection Name: "Listen to the River: St. Louis '71 '72 '73"
5. User selects "Official Release" in the Album Type combobox
6. Folder Name Preview shows: `1971-07-02 - Fox Theatre - St. Louis, MO : Listen to the River: St. Louis '71 '72 '73`
7. Normalize, review, import
8. Repeat for next concert — Collection Name remembered from settings

### Official Release (Dave's Picks Series)

1. User selects folder
2. MusicBrainz or manual entry for album info
3. Album Name: "Dave's Picks Vol. 57"
4. Date: performance date (e.g., 1978-02-01)
5. Venue/City filled
6. Album Type: Official Release
7. Folder Name Preview shows: `1978-02-01 - Uptown Theatre - Chicago, IL : Dave's Picks Vol. 57`
8. Import

---

## MusicBrainz Integration (Simplified)

### Fingerprint Lookup Flow

1. User clicks **🔎 MusicBrainz Lookup**
2. Service fingerprints first 3 tracks (using configured fpcalc.exe path)
3. If fpcalc not configured → show notification with link to Settings
4. If matches found → show Release Selector dialog (existing)
5. User selects release
6. Service fetches track data via `/ws/2/release/{releaseId}?inc=recordings+artists&fmt=json`
7. Album fields update: Artist, Album Name, Year, Artwork
8. Title column updates with MusicBrainz track names (overwrites current titles)
9. Status bar updates: "MusicBrainz: Found 14 tracks for Skull & Roses (2003)"

### Manual Search Flow

1. User clicks dropdown arrow next to MusicBrainz button (or a "Manual Search" sub-option)
2. Album Search dialog opens (existing)
3. Results → Release Selector → same flow as above from step 5

### No Confirmation Dialog

The old MusicBrainzConfirmationDialog is removed. The user sees changes directly in the Title column and album fields. To undo, click Read from Files to reload original file metadata.

---

## Normalization Behavior (Updated)

When **✨ Normalize All** is clicked:

1. For each track, take the current `SongName` (without date)
2. Run through normalization pipeline:
   - Strip existing date suffixes
   - Convert slash dates to yyyy-MM-dd
   - Strip venue info from parentheticals
   - Fuzzy match to song database
   - Fix character encoding (apostrophes, dashes)
3. Update `SongName` with normalized result
4. Recompute `Title` using date auto-append rule
5. Update match status (gold for unmatched, normal for matched)
6. Refresh track grid
7. Update status: "Matched X of Y songs"

**Normalization never touches the Date column.** It only affects song names.

---

## Removed Elements

The following elements from the current MainWindow are **removed** in this redesign:

| Removed | Reason |
|---------|--------|
| Album Type radio buttons (Live, Official Release, Studio Album, Box Set) | Replaced by simplified dropdown: Auto-detect, Audience Recording, Official Release |
| Side panel layout for album info | Replaced by horizontal album info bar |
| Preview Metadata column | Replaced by single editable Title column |
| Song column (read-only original) | No longer needed — Title IS the editable final value |
| SELECTED TRACK section (Title/Date/Segue below grid) | Replaced by inline editing in the grid |
| MusicBrainzConfirmationDialog | No longer needed — changes visible directly in Title column |
| HasMusicBrainzData flag on TrackInfo | No longer needed — no separate preview column to protect |
| Conditional field visibility based on album type | All fields always visible |
| Four album types (Live, Studio, Official, BoxSet) | Simplified to two: AudienceRecording, OfficialRelease |

---

## Preserved Elements

| Preserved | Notes |
|-----------|-------|
| Playback controls | Already implemented, just repositioned |
| Artwork panel | Moved to right side, keeps Change/Remove buttons |
| Release Selector dialog | Still used for choosing between MusicBrainz releases |
| Album Search dialog | Still used for manual MusicBrainz search |
| All service classes | MetadataService, NormalizationService, MusicBrainzService, LibraryImportService — no changes to service layer |
| Song database and fuzzy matching | No changes |
| Segue checkbox | Moved inline to track grid rows |
| Info File Viewer | Still accessible from action bar |

---

## Implementation Notes

### WPF DataGrid Inline Editing

Use WPF DataGrid with `DataGridTemplateColumn` for the Title and Date columns to support double-click inline editing. The Title column should use a `TextBlock` in display mode and `TextBox` in edit mode.

### Date Auto-Append

Implement as a computed property or method that generates the display title from `SongName` + `TrackDate` + album date comparison. This should run on:
- Any change to a track's SongName
- Any change to a track's TrackDate
- Any change to the Album Date field

### Color Coding

- Unmatched songs: Foreground = #D7BA7D (gold)
- Inherited dates: Foreground = #555555 (dim gray), FontStyle = Italic
- Override dates: Foreground = #E0E0E0 (white), FontWeight = Normal

### Keyboard Navigation

- Tab through album info fields
- Arrow keys navigate track grid
- Enter on a selected track starts inline edit
- Escape cancels inline edit
- Space toggles segue checkbox on selected row

---

## Files Affected

### Modified
- `MainWindow.xaml` — Complete layout rewrite
- `MainWindow.xaml.cs` — Rewrite event handlers for new layout
- `Models/TrackInfo.cs` — Add SongName, TrackDate; remove PreviewMetadata, HasMusicBrainzData
- `Models/AlbumInfo.cs` — Simplify to unified fields

### Removed
- `MusicBrainzConfirmationDialog.xaml/.xaml.cs` — No longer needed
- `documentation/09-musicbrainz-confirmation-dialog.md` — Remove

### Updated Documentation
- `documentation/01-main-window.md` — Complete rewrite to match new layout
- `documentation/14-musicbrainz-service.md` — Update workflow description
- `documentation/12-normalization-service.md` — Update to reflect single-column behavior
- `CLAUDE.md` — Update lookup table if doc numbers change

---

## Success Criteria

A user should be able to:

1. Open a folder and immediately see track titles ready for editing
2. Click MusicBrainz to populate data, then Normalize to clean it — two clicks
3. See exactly what will be written to files at all times (Title column = final metadata)
4. Edit any title or date by double-clicking directly in the grid
5. Handle mixed-date albums (bonus tracks from different concerts) with per-track date overrides
6. Know which songs need attention (gold = unmatched)
7. Know which dates are inherited vs overridden (gray vs white)
8. Listen to any track to verify it matches its title
9. Import without ever needing to think about "album type" (unless they want to override)
