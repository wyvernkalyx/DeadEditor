# DeadEditor - Pending Tasks

## Critical Bugs

### 1. Pause Button Not Working
**Priority:** CRITICAL
**Status:** Not Fixed
**Description:** Pause functionality doesn't work via any method (media keys or UI button). Playback continues and the only way to stop is closing the entire application.

**Reproduction Steps:**
1. Launch app, go into concert, start playing
2. Hit import button - song keeps playing
3. Use pause button (media key) - doesn't work
4. Close import screen, hit pause button - doesn't work
5. Manually click pause button in app - doesn't work
6. Have to shut down entire app to stop playback

**Possible Causes:**
- Media key routing issue when multiple windows are open
- AudioPlayerService state management issue
- Event handler not properly connected

---

## Normalization Issues

### 2. Peggy-O Not Matching
**Priority:** Medium
**Status:** Not Fixed
**Description:** "Peggy-O" fails to match during import even though it exists in songs.json database.

**Database Entry:**
```json
{
  "OfficialTitle": "Peggy-O",
  "Aliases": ["Peggy O", "Fennario"]
}
```

**Possible Cause:** Normalization algorithm issue with hyphenated names.

---

## Enhancements

### 3. Integrate Comprehensive Song Database
**Priority:** High
**Status:** Pending
**Description:** User provided a comprehensive 586-song JavaScript array from a previous project attempt. Current database only has 110 songs.

**Next Steps:**
1. Convert JavaScript array to JSON format
2. Merge with existing songs.json
3. Remove duplicates
4. Validate all entries

**Benefits:** Will dramatically improve normalization success rate and reduce manual corrections during import.

**Source File Location:** User has the 586-song array ready to provide.

---

## Technical Debt

### 4. Background Process Management
**Priority:** Low
**Status:** Ongoing
**Description:** Multiple dotnet.exe and DeadEditor.exe processes accumulate during development sessions.

**Workaround:** Manually kill processes with:
```bash
taskkill //F //IM DeadEditor.exe //T
taskkill //F //IM dotnet.exe //T
```

---

## Recently Completed (Session 2026-01-06)

- ✅ Edit Metadata functionality - Edit already-imported concerts
- ✅ Media key support - Keyboard play/pause/stop/next/previous buttons
- ✅ Improved concert view - Full metadata display (title with segue and date)
- ✅ Fixed segue display in library browser
- ✅ Expanded song database from 102 to 110 songs
- ✅ Real-time preview updates when editing track metadata

**Commit:** `20eec7b` - "Add Edit Metadata, media keys, expanded song database, and UI improvements"
