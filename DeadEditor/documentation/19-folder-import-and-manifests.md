# Spec: Folder-Based Import & Metadata Manifests

---

## 1. Problem Statement

The current import pipeline splits files across date-based folders in 
the library. A multi-date official release like Road Trips Vol. 2 No. 2 
(which contains tracks from 1968-01-17, 1968-01-20, 1968-01-23, and 
1968-02-02) gets scattered into four separate folders by date. This:

- Breaks the relationship between files that were imported together
- Makes it impossible to view or manage the album as a unit
- Creates confusion when the same date appears in multiple albums
- Loses the user's intent: "these files belong together"

Additionally, when the user verifies metadata (normalizes song names, 
fixes venues, sets album types), that work is lost if they need to 
re-import. There's no way to capture and replay verified metadata.

## 2. Core Principles

### Principle 1: Folder = Unit of Import
A folder selected for import is an atomic unit. ALL files in that 
folder are copied together into ONE library folder. They are never 
split, reorganized, or scattered by date. The folder structure the 
user selected is the folder structure they get in the library.

### Principle 2: Album Type is a Tag, Not a Folder Decision
"Audience Recording" vs "Official Release" is metadata for filtering 
and searching. It does NOT determine where files are stored or how 
they're grouped. Both types follow the same import path.

### Principle 3: By Date is a Virtual View
The "By Date" view in the Library Grid is a cross-cutting view that 
shows all tracks from all albums that match a given date. A track 
from Road Trips Vol. 2 No. 2 dated 1968-02-02 AND a standalone 
audience recording from 1968-02-02 would both appear under that date. 
But they remain in their separate physical folders.

### Principle 4: Verified Metadata is Precious
When a user normalizes song names, fixes venues, verifies setlists, 
and saves — that work should be capturable in a manifest file that 
can be used to restore the same metadata on re-import.

## 3. Import Pipeline — Revised

### Current (broken):
```
User selects folder → Read files → Split by date → Create date folders 
in library → Copy files to date folders → Write tags
```

### Proposed:
```
User selects folder → Read files → Copy ALL files to ONE library folder 
→ Write tags → Generate manifest JSON
```

### Library Folder Naming Convention

The destination folder name is auto-generated from metadata:

**For audience recordings:**
```
{LibraryRoot}/{yyyy}/{yyyy-MM-dd} - {Venue} - {City}, {State}
```
Example: `Library/1971/1971-02-19 - Capitol Theatre - Port Chester, NY`

**For official releases:**
```
{OfficialReleasesPath}/{Series or Album Name}
```
Example: `OfficialReleases/Road Trips/Road Trips Vol. 2 No. 2`

**Key change:** For multi-date official releases, ALL tracks go in 
ONE folder regardless of individual track dates. The tracks carry 
their own date metadata in the tags.

### What About Multi-Folder Box Sets?

Box sets like "Enjoying the Ride" or "Get Shown the Light" span 
multiple physical source folders. Each source folder is imported 
separately into its own library folder. The composite identity key 
(Album + AlbumType) groups them in the Library Grid as one album.

This doesn't change — multi-folder albums remain multi-folder. The 
key change is that we don't further split a SINGLE folder's contents 
across dates.

## 4. Metadata Manifests

### What Is a Manifest?

A JSON file that captures the verified metadata state of an imported 
album. It's a sidecar file stored next to the album folder in the 
library (not inside the audio folder).

### Manifest Location

Sidecar file adjacent to the album folder:
```
{LibraryRoot}/1971/1971-02-19 - Capitol Theatre - Port Chester, NY.json
{OfficialReleasesPath}/Road Trips/Road Trips Vol. 2 No. 2.json
```

The manifest filename matches the album folder name with a `.json` 
extension. This keeps manifests paired with their albums without 
cluttering the audio folders.

### Manifest Contents

```json
{
  "version": 1,
  "folderName": "1971-02-19 - Capitol Theatre - Port Chester, NY",
  "albumName": "",
  "albumType": "AudienceRecording",
  "artist": "Grateful Dead",
  "date": "1971-02-19",
  "venue": "Capitol Theatre",
  "city": "Port Chester",
  "state": "NY",
  "verifiedAt": "2026-04-04T12:00:00Z",
  "tracks": [
    {
      "filename": "01 - Morning Dew (1971-02-19).flac",
      "trackNumber": 1,
      "discNumber": 1,
      "title": "Morning Dew (1971-02-19)",
      "songName": "Morning Dew",
      "trackDate": "1971-02-19",
      "segue": false
    },
    {
      "filename": "02 - Dark Star > (1971-02-19).flac",
      "trackNumber": 2,
      "discNumber": 1,
      "title": "Dark Star > (1971-02-19)",
      "songName": "Dark Star",
      "trackDate": "1971-02-19",
      "segue": true
    }
  ]
}
```

### When Manifests Are Generated

- **On every save:** After saving changes in Edit Metadata, the 
  manifest is automatically updated (or created if it doesn't exist).
- **On import:** After the user completes import, a manifest is 
  generated capturing the imported state.

### When Manifests Are Used — Import Matching

When importing a folder, the import pipeline checks if a manifest 
exists that matches the album's date. If a match is found:

- Show a **compare view** displaying the differences between the 
  incoming files and the manifest's verified metadata.
- The user can review and selectively apply manifest data.
- This handles cases where different rips of the same show have 
  different track counts (e.g., segued songs merged vs split).

The compare/apply UI is a future feature (implementation step 3). 
For now, manifests are generated on save and stored for future use.

### Track Count Mismatch Handling

Different recordings of the same show can have different track counts:
- One rip has "Dark Star > The Other One" as two tracks
- Another rip has "Dark Star > The Other One" as one merged track

The manifest compare view must handle this gracefully:
- Show track-by-track comparison with fuzzy matching by song name
- Allow the user to map manifest tracks to incoming tracks
- Never blindly overwrite — always show what would change

## 5. Library Loading — No Changes Needed

The library already creates one LibraryShow per folder. The key 
change is that the IMPORT no longer splits a folder's files across 
dates, so the library load works correctly with intact folders.

The By Date view already explodes albums into per-date rows by 
reading individual track dates. This continues to work.

## 6. Migration Path

### For the current testing cycle:
1. Implement the import fix (stop splitting by date)
2. Implement manifest generation on import and save
3. Clear and re-import the test library
4. Manifests capture verified work going forward

### For future re-imports:
When importing, check for a manifest matching the date. If found, 
offer the compare/apply UI. This preserves verified work across 
re-imports.

## 7. Implementation Order

1. **Fix import to preserve folders** — stop splitting by date, 
   copy all files to one destination folder
2. **Manifest generation** — auto-generate on import and save
3. **Manifest-aware import** — detect matching manifests on import, 
   show compare/apply UI
4. **Bulk operations** — lower priority, design when needed

---

**Last Updated:** 2026-04-04
**Status:** Implementation steps 1-2 (import fix + manifest generation)
