# Import View Segue Inconsistency Diagnostic — 2026-04-25

## 1. MB Lookup write path

### Handler location
[ImportView.xaml.cs:1256](Views/ImportView.xaml.cs#L1256) — `MusicBrainzButton_Click` initiates the lookup. After the user selects a release from the `ReleaseSelectorDialog`, it calls `ApplyMusicBrainzData(selector.SelectedRelease)` at line 1293.

### Where SongName gets written
[ImportView.xaml.cs:1363](Views/ImportView.xaml.cs#L1363) — Inside `ApplyMusicBrainzData`, the loop at line 1355-1369 matches local tracks to MB tracks by disc + position:

```csharp
if (mbTrack != null)
{
    localTrack.Track.SongName = mbTrack.Title;    // LINE 1363 — direct assignment
    localTrack.Track.IsMatched = true;
    localTrack.Track.IsModified = true;
    localTrack.UpdateDisplayTitle();
    matchedCount++;
}
```

**`mbTrack.Title` is the raw string from MusicBrainz.** For a box set like Dick's Picks Vol. 22, MusicBrainz returns titles like `"Dark Star"`, `"The Eleven"`, etc. — typically **without** segue markers. However, some MB releases DO include `>` in their titles for segued tracks (e.g. `"Dark Star >"`). The title is written verbatim to `SongName`.

### Where HasSegue gets written (if at all)
**It is NOT written.** There is no code in `ApplyMusicBrainzData` that sets `Track.Segue` or `Track.HasSegue`. The segue checkbox state after MB Lookup depends entirely on whatever value was set during the initial `ReadFolder` call.

### Whether ParseTitleAndDate is called
**No.** `ApplyMusicBrainzData` does not call `ParseTitleAndDate` on the MB title before writing to `SongName`. The MB title is assigned directly.

### The bug mechanism
When MB returns a title containing a segue marker (e.g. `"Dark Star >"`), this `>` becomes part of `SongName`. Meanwhile:
- `Track.Segue` retains whatever value it had from `ReadFolder` (set at [MetadataService.cs:122](Services/MetadataService.cs#L122) via `HasSegueMarker(rawTitle)`)
- If the original FLAC title didn't have a segue marker but MB returned one, `Segue` = `false` while `SongName` contains `>`
- If the original FLAC title did have a segue marker, `Segue` = `true` and MB wrote another `>` into `SongName`, so `UpdateDisplayTitle()` at [TrackInfoViewModel.cs:148-149](Models/TrackInfoViewModel.cs#L148-L149) appends **another** `>` — resulting in `"Dark Star > >"` (double segue)

For the observed bug (segue `>` in title, checkbox unchecked):
1. Original FLAC files likely had clean titles without segues (e.g. `"01 Dark Star.flac"` with TITLE tag = `"Dark Star"`)
2. `ReadFolder(editMode: false)` called `ParseTitleAndDate("Dark Star")` → `SongName = "Dark Star"`, `HasSegue = false`
3. MB Lookup returned `"Dark Star >"` from MusicBrainz (the `>` indicates a segue in MB's data)
4. `SongName` was set to `"Dark Star >"` directly, but `Segue` remained `false`
5. `UpdateDisplayTitle()` builds: `"Dark Star >"` (from SongName) + no segue arrow (Segue is false) + `" (1968-02-23)"` (from date) = `"Dark Star > (1968-02-23)"`
6. Checkbox shows unchecked because `Segue == false`

**State/display disagreement confirmed.** The `>` is baked into `SongName` but not reflected in `Segue`.

## 2. Edit Metadata load path comparison

After import + saving + reopening in Edit Metadata, segues are correctly populated because of the fix applied from the double-date diagnostic:

[EditMetadataView.xaml.cs:147-153](Views/EditMetadataView.xaml.cs#L147-L153):
```csharp
foreach (var track in rawTracks)
{
    var (cleanName, date) = _metadataService.ParseTitleAndDate(track.SongName);
    track.SongName = cleanName;
    if (!string.IsNullOrEmpty(date) && string.IsNullOrEmpty(track.TrackDate))
        track.TrackDate = date;
}
```

**However**, this fix does NOT explicitly set `Segue`! The `ParseTitleAndDate` method strips trailing segue markers from the song name (see §3), but the `Segue` boolean is set earlier by `ReadFolder(editMode: true)` at [MetadataService.cs:98](Services/MetadataService.cs#L98):
```csharp
HasSegue = HasSegueMarker(rawTitle),  // Detect from title for display
```

So in Edit Metadata's load path:
1. `ReadFolder(editMode: true)` reads the saved FLAC TITLE tag (e.g., `"Dark Star > (1968-02-23)"`) and sets `HasSegue = true` via `HasSegueMarker`
2. The `ParseTitleAndDate` strip-on-read at line 149 cleans `SongName` to `"Dark Star"` and sets `TrackDate = "1968-02-23"`
3. Result: `SongName = "Dark Star"`, `Segue = true`, `TrackDate = "1968-02-23"` — **all consistent**

The key difference: **Edit Metadata reads from saved FLAC tags that already have the segue baked in**, so `HasSegueMarker` detects it. In Import view, the segue comes from MB data applied _after_ the initial `ReadFolder`, so `HasSegueMarker` never sees it.

## 3. ParseTitleAndDate return shape

[MetadataService.cs:488](Services/MetadataService.cs#L488):
```csharp
public (string songName, string? date) ParseTitleAndDate(string title)
```

Returns `(string songName, string? date)` — a 2-tuple.

**Segue stripping behavior:** Every pattern branch strips trailing segue markers from the returned `songName` using:
```csharp
songName = Regex.Replace(songName, @"(\s*[-–]?\s*>)+\s*$", "").Trim();
```

The final fallback at [line 631](Services/MetadataService.cs#L631) also strips segues even when no date is found:
```csharp
var cleanTitle = Regex.Replace(title, @"(\s*[-–]?\s*>)+\s*$", "").Trim();
return (cleanTitle, null);
```

**Key finding:** `ParseTitleAndDate` strips segue markers from `SongName` but does **not** return segue info as a separate field. There is no `bool hasSegue` in its return tuple. The segue information is simply discarded during parsing. This means callers must detect segues separately (via `HasSegueMarker`) before calling `ParseTitleAndDate`, or the segue state must be set by another mechanism.

## 4. MB API response inspection

[MusicBrainzService.cs:828-835](Services/MusicBrainzService.cs#L828-L835) — `GetReleaseTracksAsync` builds `MusicBrainzTrack` objects:
```csharp
tracks.Add(new MusicBrainzTrack
{
    DiscNumber = discNumber,
    Position = position,
    Title = trackTitle,     // Raw title from MB JSON
    Length = length
});
```

**MusicBrainz does NOT provide segue information as a separate field.** The `MusicBrainzTrack` model ([ReleaseSelectorDialog.xaml.cs:69-75](ReleaseSelectorDialog.xaml.cs#L69-L75)) has no segue field. Any segue info from MB is embedded in the title string itself (e.g., `"Dark Star >"` or `"Dark Star > The Eleven"`).

Whether MB actually includes segue markers depends on how the specific release was entered into the MusicBrainz database. Some releases have them, some don't. This is inconsistent across releases.

## 5. TrackInfoViewModel.HasSegue plumbing

[TrackInfoViewModel.cs:110-118](Models/TrackInfoViewModel.cs#L110-L118):
```csharp
public bool Segue
{
    get => Track.Segue;
    set
    {
        Track.Segue = value;
        OnPropertyChanged();
        UpdateDisplayTitle();
    }
}
```

- `Segue` is a direct passthrough to `Track.Segue`
- Setting `Segue` triggers `PropertyChanged` and `UpdateDisplayTitle()`, so the checkbox AND the displayed title will both update correctly if `Segue` is set
- `Track.Segue` also has its own `PropertyChanged` notification ([TrackInfo.cs:86-98](Models/TrackInfo.cs#L86-L98)) which triggers `TrackInfoViewModel.Track_PropertyChanged` ([TrackInfoViewModel.cs:35](Models/TrackInfoViewModel.cs#L35)) → also calls `UpdateDisplayTitle()`

The DataGrid's segue checkbox binds to `Segue` (the ViewModel property). The display title is computed in `UpdateDisplayTitle()` at [line 147-149](Models/TrackInfoViewModel.cs#L147-L149):
```csharp
if (Track.Segue)
{
    songName += " >";
}
```

**Conclusion:** Setting `Track.Segue = true/false` will correctly update both the checkbox and the display title via property change propagation. The plumbing is sound.

## 6. Other write paths with same bug pattern

### 6.1 ImportView — Match Setlist
[ImportView.xaml.cs:1192-1194](Views/ImportView.xaml.cs#L1192-L1194):
```csharp
if (setlistSongs[matchedIndex].Segue)
{
    tw.Track.Segue = true;
```
**No bug here.** Match Setlist writes canonical titles from the setlist (clean names, no embedded segues), AND explicitly sets `Segue` from setlist data. However: it only sets `Segue = true`, never `false`. If a track previously had `Segue = true` (from ReadFolder) but the setlist says it shouldn't, the stale `true` persists. This is a minor issue — in practice, the initial ReadFolder for import mode calls `ParseTitleAndDate` which strips segues from `SongName`, so `HasSegueMarker` detects segue from the original raw title, which is generally correct.

### 6.2 ImportView — Match to Song
[ImportView.xaml.cs:814-818](Views/ImportView.xaml.cs#L814-L818):
```csharp
vm.Track.SongName = selectedSong.Canonical;
if (selectedSong.Segue)
    vm.Track.Segue = true;
```
**Same minor issue as 6.1** — only sets `Segue = true`, never `false`. But canonical titles from setlist are clean, so no embedded `>` in `SongName`.

### 6.3 EditMetadataView — ApplyMbTrackTitles
[EditMetadataView.xaml.cs:1351](Views/EditMetadataView.xaml.cs#L1351):
```csharp
localTrack.SongName = mbTrack.Title;
```
**Same bug as Import view.** MB title is written directly to `SongName` without calling `ParseTitleAndDate` or setting `Segue`. However, this is followed by `ReconstructRawTitles()` at [line 1358](Views/EditMetadataView.xaml.cs#L1358), which rebuilds `RawTitle` from `SongName + Segue + TrackDate`. If `SongName` has embedded `>`, `ReconstructRawTitles` won't strip it — the `>` will persist in both `SongName` and `RawTitle`.

### 6.4 EditMetadataView — Match Setlist
[EditMetadataView.xaml.cs:1114](Views/EditMetadataView.xaml.cs#L1114):
```csharp
track.SongName = setlistSongs[matchedIndex].Canonical;
track.Segue = setlistSongs[matchedIndex].Segue;
```
**No bug.** Sets both `SongName` (clean canonical) and `Segue` explicitly (including `false`).

### 6.5 EditMetadataView — Match to Song
[EditMetadataView.xaml.cs:1025](Views/EditMetadataView.xaml.cs#L1025):
```csharp
track.SongName = selectedSong.Canonical;
```
Same minor pattern — writes canonical (clean) title. Segue is only set to `true`, not `false`. Low risk since canonical titles don't contain `>`.

## 7. Recommended fix

### Primary fix: Parse MB titles before writing to SongName

**Location:** [ImportView.xaml.cs:1363](Views/ImportView.xaml.cs#L1363) inside `ApplyMusicBrainzData`

**Change the assignment block** from:
```csharp
localTrack.Track.SongName = mbTrack.Title;
localTrack.Track.IsMatched = true;
localTrack.Track.IsModified = true;
localTrack.UpdateDisplayTitle();
```

**To:**
```csharp
var (cleanName, mbDate) = _metadataService.ParseTitleAndDate(mbTrack.Title);

// Detect segue from the MB title BEFORE stripping
bool mbHasSegue = _metadataService.HasSegueMarker(mbTrack.Title);  // NOTE: HasSegueMarker is currently private

localTrack.Track.SongName = cleanName;

// Only set Segue to true from MB data — don't overwrite user-set segue with false
if (mbHasSegue)
    localTrack.Track.Segue = true;

// Apply MB-extracted date if track doesn't already have one
if (!string.IsNullOrEmpty(mbDate) && string.IsNullOrEmpty(localTrack.Track.TrackDate))
    localTrack.Track.TrackDate = mbDate;

localTrack.Track.IsMatched = true;
localTrack.Track.IsModified = true;
localTrack.UpdateDisplayTitle();
```

**Note:** `HasSegueMarker` is currently `private` in `MetadataService`. It would need to be made `public` (or `internal`) for the Import view to call it. Alternatively, inline the regex check.

### Same fix needed in EditMetadataView.ApplyMbTrackTitles

**Location:** [EditMetadataView.xaml.cs:1351](Views/EditMetadataView.xaml.cs#L1351)

Apply the same `ParseTitleAndDate` + segue detection pattern before writing `SongName`.

### Alternative: Add segue detection to ParseTitleAndDate's return

Change the return type from `(string songName, string? date)` to `(string songName, string? date, bool hasSegue)`. This would be cleaner but requires updating all existing call sites (approximately 5 callers). Since the segue strip already happens inside `ParseTitleAndDate`, detecting it there is natural.

### What NOT to do
- Do NOT strip segues inside `UpdateDisplayTitle()` — that would mask dirty state rather than fixing it
- Do NOT change `ReadFolder` — it correctly detects segues from the raw FLAC title
- Do NOT change the MB API response parsing — the data from MB is what it is

## 8. Open questions

1. **Should `HasSegueMarker` be made public?** It's a simple utility (`Regex.IsMatch(title, @"[-–]?>|→|\[>\]")`) and would be useful for any code path that receives external titles. Alternatively, the regex could be a `public static` constant.

2. **What does MusicBrainz actually return for Dick's Picks Vol. 22?** The user observed `"Dark Star >"` in the grid, but we need to verify: did MB return `"Dark Star >"` (with trailing `>`), or did MB return `"Dark Star"` and the `>` was already in `SongName` from a prior step? The debug output `[MatchSetlist-BEFORE]` could answer this if Match Setlist ran before MB Lookup.

3. **Should the fix also handle the case where MB returns a title WITH a date suffix?** Some MusicBrainz releases include `(Live at Venue, M/D/YYYY)` in their titles. `ParseTitleAndDate` handles this (Pattern 1), but the current Import view code would write the entire string into `SongName`, creating both embedded dates and duplicate dates in the display. The fix (calling `ParseTitleAndDate`) handles this automatically.

4. **Track-number matching in ApplyMusicBrainzData is fragile.** At [line 1357-1359](Views/ImportView.xaml.cs#L1357-L1359), matching uses `localTrack.DiscNumber` and `localTrack.TrackNumber` directly against MB's disc/position. But `TrackNumber` in DeadEditor uses the disc-aware convention (101, 201, etc.), while MB's `Position` is 1-based within disc. If a Match Setlist already ran and renumbered tracks, the positions might not align with MB's expectations. This is separate from the segue bug but worth noting.

5. **Should we validate that MB-returned titles pass normalization?** MB titles may use different canonical names than what's in `songs.json`. After applying MB titles, should the code auto-run normalization to ensure consistency with the song database?
