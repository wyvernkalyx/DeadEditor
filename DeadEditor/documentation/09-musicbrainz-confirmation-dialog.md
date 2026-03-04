# Feature Documentation: MusicBrainz Confirmation Dialog

## Purpose

The **MusicBrainz Confirmation Dialog** shows users exactly what fields will be populated after selecting a MusicBrainz release, allowing them to review and confirm changes before they're applied. This dialog provides transparency about what data will be imported and gives users final approval before modifying their metadata.

## How It's Opened

**Trigger:** User selects a release from the Release Selector Dialog

**Caller:** [MainWindow.xaml.cs](MainWindow.xaml.cs) (after user selects from ReleaseSelectorDialog)
```csharp
// After user selects a release from ReleaseSelectorDialog
var selectedRelease = selectorDialog.SelectedRelease;
if (selectedRelease != null)
{
    var confirmDialog = new MusicBrainzConfirmationDialog(selectedRelease, _albumInfo.Type, _tracks.Count)
    {
        Owner = this
    };

    if (confirmDialog.ShowDialog() == true)
    {
        // Apply the MusicBrainz data to fields
    }
}
```

**Parameters:**
- `ReleaseOption selectedRelease` - The selected MusicBrainz release
- `AlbumType albumType` - Current album type (Live, Studio, OfficialRelease, BoxSet)
- `int trackCount` - Number of tracks in the import

**Modal Behavior:** Yes (ShowDialog blocks until user confirms or cancels)

**Return Value:** `DialogResult` (true if "Apply Changes" clicked, false if "Cancel" clicked)

---

## Screen Layout

### Visual Structure
A centered modal dialog (600x500px, not resizable) with **dark theme (#1E1E1E background, white text)**:

**Header Section:**
- Dialog title: "MusicBrainz Match Details"
- Subtitle: "Review what will be imported from MusicBrainz"

**Release Information Section** (read-only TextBlocks):
- **Title:** {selectedRelease.Title}
- **Artist:** {selectedRelease.Artist}
- **Year:** {selectedRelease.Year}
- **Label:** {selectedRelease.Label}
- **Country:** {selectedRelease.Country}
- **Format:** {selectedRelease.Format}

**Fields That Will Be Updated Section:**
- Dynamic list based on album type
- **Studio Album:**
  * ✓ Album Name → "{selectedRelease.Title}"
  * ✓ Artist → "{selectedRelease.Artist}"
  * ✓ Release Year → "{selectedRelease.Year}"
  * ✓ Artwork (if available)
  * ✓ Track Titles ({trackCount} tracks)
- **Box Set:**
  * ✓ Box Set Name → "{selectedRelease.Title}"
  * ✓ Artist → "{selectedRelease.Artist}"
  * ✓ Release Year → "{selectedRelease.Year}"
  * ✓ Artwork (if available)
  * ✓ Track Titles ({trackCount} tracks)
- **Official Release:**
  * ✓ Official Release → "{selectedRelease.Title}"
  * ✓ Artist → "{selectedRelease.Artist}"
  * ✓ Release Year → "{selectedRelease.Year}"
  * ✓ Artwork (if available)
  * ✓ Track Titles ({trackCount} tracks)
- **Live Recording:**
  * ✓ Artist → "{selectedRelease.Artist}"
  * ✓ Release Year → "{selectedRelease.Year}"
  * ✓ Artwork (if available)
  * ✓ Track Titles ({trackCount} tracks)
  * ℹ️ Date, Venue, City, State will remain unchanged

**Track Information:**
- "{selectedRelease.Tracks?.Count ?? 0} MusicBrainz tracks will populate the Preview Metadata column"
- "Your original file-based track titles will remain in the Song column for comparison"

**Button Section:**
- **Apply Changes** (primary button, green) - Accepts and applies the MusicBrainz data
- **Cancel** (secondary button, gray) - Rejects and closes without applying

---

## Interactive Elements

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Apply Changes | Button | `ApplyButton` | Sets DialogResult = true, closes dialog | N/A | Pending |
| Cancel | Button | `CancelButton` | Sets DialogResult = false, closes dialog | N/A | Pending |

---

## Business Rules

1. **Album-Type-Specific Display:**
   - Dialog content dynamically changes based on `albumType` parameter
   - Only shows fields that will actually be updated for that type

2. **Track Count Display:**
   - Shows number of MusicBrainz tracks available
   - Warns if track count doesn't match file count

3. **No Changes Without Confirmation:**
   - Clicking "Cancel" or closing dialog (X button) returns false
   - No metadata is applied unless user explicitly clicks "Apply Changes"

4. **Read-Only Preview:**
   - All information is read-only
   - User cannot edit values in this dialog
   - Changes are applied in MainWindow after confirmation

---

## User Experience

**Workflow:**
1. User performs MusicBrainz lookup (fingerprint or manual search)
2. User selects release from Release Selector Dialog
3. **NEW:** Confirmation dialog appears showing what will change
4. User reviews the details
5. User clicks "Apply Changes" to proceed or "Cancel" to reject
6. If applied, MainWindow updates fields and shows success status

**Benefits:**
- **Transparency:** User sees exactly what fields will be populated
- **Control:** User has final approval before any changes
- **Confidence:** Reduces accidental overwrites
- **Clarity:** Album-type-specific information prevents confusion

---

## Implementation Notes

**Constructor:**
```csharp
public MusicBrainzConfirmationDialog(ReleaseOption selectedRelease, AlbumType albumType, int trackCount)
{
    InitializeComponent();

    // Display release information
    TitleTextBlock.Text = selectedRelease.Title;
    ArtistTextBlock.Text = selectedRelease.Artist;
    // ... etc

    // Build field update list based on albumType
    BuildFieldUpdateList(selectedRelease, albumType);

    // Display track information
    TrackInfoTextBlock.Text = $"{selectedRelease.Tracks?.Count ?? 0} MusicBrainz tracks...";
}
```

**Styling:**
- Consistent dark theme with MainWindow
- Clear visual hierarchy (headers, sections, buttons)
- Green "Apply" button to indicate positive action
- Gray "Cancel" button for neutral action

---

## Testing Checklist

- [ ] Dialog displays correctly for Studio Album type
- [ ] Dialog displays correctly for Box Set type
- [ ] Dialog displays correctly for Official Release type
- [ ] Dialog displays correctly for Live Recording type
- [ ] "Apply Changes" returns DialogResult = true
- [ ] "Cancel" returns DialogResult = false
- [ ] X button (close) returns DialogResult = false
- [ ] Track count displays correctly
- [ ] All release fields populate from ReleaseOption
