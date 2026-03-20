# Player Window — Winamp-Style Global Playback

## Overview

A single global playback system replacing the current fragmented controls.
Consists of three components: PlaybackService (singleton), PlayerWindow, and
PlaylistWindow. A VIS (visualizer) window is stubbed for future use.

---

## PlaybackService

Singleton registered at app startup in App.xaml.cs.

**Exposes:**
- `CurrentTrack` (TrackInfo)
- `PlaybackState` (Playing / Paused / Stopped)
- `Position` (TimeSpan)
- `Duration` (TimeSpan)
- `Playlist` (ObservableCollection<TrackInfo>)

**Methods:** `Play(TrackInfo)`, `Pause()`, `Stop()`, `Next()`, `Previous()`, `Seek(TimeSpan)`, `LoadPlaylist(IEnumerable<TrackInfo>)`

**Events:** `TrackChanged`, `PlaybackStateChanged`, `PositionChanged`

All NAudio logic lives here. No other class instantiates an audio player.

---

## PlayerWindow Layout

Chromeless WPF Window (no standard title bar). Compact fixed-width panel.

```
┌─────────────────────────────────────────────┐
│ ≡  [drag area]                      [_] [■] │  <- thin drag bar, minimize, undock
│  «   ▶   ■   ⏸   »  │ PL │ VIS │          │  <- transport + PL/VIS toggles
│ ████████████████████░░░░  0:00 / 4:32       │  <- seek bar + elapsed/total time
│ ┌──────────────────────────────────────┐    │
│ │  Dark Star (1969-02-27) — Fillmore   │◄── │  <- scrolling marquee
│ └──────────────────────────────────────┘    │
│ VOL: ────●──────────  BAL: ─────●────       │  <- volume + balance sliders
└─────────────────────────────────────────────┘
```

**Transport button symbols (Unicode only — no icon fonts, no Segoe MDL2):**

| Button | Symbol | Notes |
|--------|--------|-------|
| Previous | « | FontSize 14 |
| Play | ▶ | FontSize 16 |
| Stop | ■ | FontSize 14 |
| Pause | ⏸ | FontSize 14 |
| Next | » | FontSize 14 |

All button text set directly in XAML Foreground/FontSize — no loaded resources.
This prevents the "disappearing button graphics" bug caused by unreliable glyph loading.

**Marquee:** Canvas + TranslateTransform animation scrolling left when text exceeds
display width. Format: `Song Name (yyyy-MM-dd) — Venue, City, State`

---

## Docking Behavior

PlayerWindow is a separate WPF Window (not a UserControl embedded in LibraryBrowserWindow).

**Docked state (default):**
- PlayerWindow floats snapped to the bottom-left corner of **LibraryBrowserWindow** (the primary app window)
- Subscribes to LibraryBrowserWindow.LocationChanged and LibraryBrowserWindow.SizeChanged
- On each event, recalculates and sets PlayerWindow.Left / PlayerWindow.Top
  to maintain the bottom-left snap position
- Snap offset formula:
  - Left = LibraryBrowserWindow.Left
  - Top = LibraryBrowserWindow.Top + LibraryBrowserWindow.Height

**Undocked state:**
- User clicks undock button (■ in drag bar)
- PlayerWindow becomes free-floating — no longer tracks LibraryBrowserWindow position
- LibraryBrowserWindow placeholder bar shows "⊞ Re-dock Player" button

**Re-docking:**
- User clicks "⊞ Re-dock Player" on LibraryBrowserWindow placeholder bar
- PlayerWindow snaps back to bottom-left of LibraryBrowserWindow
- Location tracking resumes

**LibraryBrowserWindow placeholder bar** (thin, always present at bottom of LibraryBrowserWindow):
- Docked: shows "Now Playing: [track name]" (or empty if nothing playing)
- Undocked: shows "⊞ Re-dock Player" button

**Note:** MainWindow is a modal import dialog, not the primary app window. PlayerWindow docks to LibraryBrowserWindow.

---

## PlaylistWindow Layout

Separate WPF Window. Attaches below PlayerWindow by default.

```
┌─────────────────────────────────────────────┐
│  PLAYLIST EDITOR                    [_] [■] │  <- drag bar, minimize, detach
├────┬──────────────────────────┬───────┬─────┤
│ #  │ Title                    │ Date  │Time │
├────┼──────────────────────────┼───────┼─────┤
│ 1  │ Dark Star                │ 02-27 │8:32 │  <- gold highlight = now playing
│ 2  │ St. Stephen              │ 02-27 │4:10 │
│ 3  │ The Eleven               │ 02-27 │7:44 │
├────┴──────────────────────────┴───────┴─────┤
│ [+ Add] [✕ Clear]          23 tracks  47:12 │
└─────────────────────────────────────────────┘
```

**Attachment behavior:**
- Default: attached directly below PlayerWindow, moves with it
- Detach button (■) makes it a free-floating window
- When PlayerWindow re-docks to MainWindow, PlaylistWindow re-attaches below
  PlayerWindow (unless it was manually detached in this session)
- Double-click a row → PlaybackService.Play(track)
- Currently playing row highlighted in gold (#D7BA7D)

**PL button on PlayerWindow:** toggles PlaylistWindow.Visibility

**Population rules:**
- Import screen opens an album → PlaybackService.LoadPlaylist(tracks)
- Library browser: selecting a concert + pressing Play → PlaybackService.LoadPlaylist(tracks)
- PlaylistWindow DataGrid bound directly to PlaybackService.Playlist (ObservableCollection)

---

## VIS Window (stub)

**Attachment behavior:**
- Default: attached directly below **PlaylistWindow**, moves with it
- Detach button (■) makes it a free-floating window
- Content: dark background, centered text "Visualizer — coming soon"
- VIS button on PlayerWindow toggles visibility
- Architecture is identical to PlaylistWindow so real visualizer content
  can replace the stub later with no structural changes

**Attachment chain:** PlayerWindow → PlaylistWindow → VisWindow (all follow each other when docked)

---

## What Gets Removed

- Playback controls from MainWindow (import dialog)
- Playback controls from LibraryBrowserWindow (primary app window)
- Any NAudio instantiation outside of PlaybackService

**What Gets Added:**
- Placeholder bar in LibraryBrowserWindow showing current track when docked, or "Re-dock Player" button when undocked

---

## Commit Order (Completed)

1. ✓ PlaybackService singleton + remove duplicate playback logic (no UI changes)
2. ✓ PlayerWindow + docking behavior
3. ✓ PlaylistWindow + attachment behavior
4. ✓ VIS stub window
5. ✓ Remove old controls + add LibraryBrowserWindow placeholder bar
6. ✓ Documentation updates (01-main-window.md, 02-library-browser.md, this file)

## Startup Sequence

1. App.xaml.cs OnStartup() creates windows in order:
   - LibraryBrowserWindow (primary window)
   - PlayerWindow(libraryWindow) - docks to LibraryBrowserWindow
   - PlaylistWindow(playerWindow) - attaches to PlayerWindow
   - VisWindow(playlistWindow) - attaches to PlaylistWindow
2. All windows shown at startup
3. PlayerWindow follows LibraryBrowserWindow
4. PlaylistWindow follows PlayerWindow
5. VisWindow follows PlaylistWindow

## Known Cosmetic Issue

**PlayerWindow appears orphaned during import:** When MainWindow (import modal) is open, PlayerWindow remains visible at the LibraryBrowserWindow position, appearing disconnected. This is cosmetic only - playback still works.

**Future polish options:**
- Option 1: Hide PlayerWindow when MainWindow opens, restore when MainWindow closes
- Option 2: Dock PlayerWindow to MainWindow when it's open (complex - requires window switching logic)
- **Current decision:** Leave as-is. Users understand PlayerWindow is global.
