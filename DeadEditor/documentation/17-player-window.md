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
│ ≡  [drag area]                      [_] [×] │  <- thin drag bar, minimize, close
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

## Window Positioning

PlayerWindow is a separate WPF Window (not embedded in LibraryBrowserWindow).

**Free-floating behavior:**
- PlayerWindow floats independently of LibraryBrowserWindow
- User can position it anywhere on screen by dragging
- Position is persisted to `%APPDATA%/DeadEditor/settings.json` on every move
- On app relaunch, PlayerWindow restores to last saved position
- If no saved position (first launch), defaults to center-bottom of screen (40px from bottom for taskbar)
- If saved position is off-screen (monitor disconnected), falls back to default position

**Position persistence:**
- Saved on `LocationChanged` event (only when window is in Normal state, not minimized)
- Settings keys: `PlayerWindowLeft`, `PlayerWindowTop`
- Screen bounds validation prevents restoring to invalid positions

**Close button:**
- × button in drag bar closes PlayerWindow
- User can re-open from LibraryBrowserWindow menu (or keyboard shortcut, if implemented)

**Note:** PlayerWindow no longer docks to LibraryBrowserWindow. The "Now Playing" placeholder bar has been removed from LibraryBrowserWindow.

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
- Default: attached directly below PlayerWindow, follows PlayerWindow when it moves
- Detach button (■) makes it a free-floating window with independent position
- When attached: position not persisted (always follows PlayerWindow)
- When detached: position persisted to settings (`PlaylistWindowLeft`, `PlaylistWindowTop`)
- On app relaunch: restores to detached position if saved, otherwise attaches below PlayerWindow
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
- Default: attached directly below PlaylistWindow, follows PlaylistWindow when it moves
- Detach button (■) makes it a free-floating window with independent position
- When attached: position not persisted (always follows PlaylistWindow)
- When detached: position persisted to settings (`VisWindowLeft`, `VisWindowTop`)
- On app relaunch: restores to detached position if saved, otherwise attaches below PlaylistWindow
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
- Docking system (PlayerWindow no longer snaps to LibraryBrowserWindow)
- Placeholder bar from LibraryBrowserWindow (no longer needed without docking)

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
   - PlayerWindow() - free-floating (restores last position or defaults to center-bottom)
   - PlaylistWindow(playerWindow) - attaches below PlayerWindow (or restores detached position)
   - VisWindow(playlistWindow) - attaches below PlaylistWindow (or restores detached position)
2. All windows shown at startup
3. PlayerWindow is independent (no longer follows LibraryBrowserWindow)
4. PlaylistWindow follows PlayerWindow (when attached)
5. VisWindow follows PlaylistWindow (when attached)

## Position Persistence Implementation

All three companion windows (PlayerWindow, PlaylistWindow, VisWindow) persist their positions:

**PlayerWindow:**
- Always saves position on `LocationChanged`
- Settings keys: `PlayerWindowLeft`, `PlayerWindowTop`
- Default position: center-bottom of screen (40px from bottom)
- Screen bounds validation on restore

**PlaylistWindow:**
- Saves position only when detached (`_isAttached == false`)
- When attached: clears saved position (position follows PlayerWindow)
- Settings keys: `PlaylistWindowLeft`, `PlaylistWindowTop`
- Restoring saved position implies detached state

**VisWindow:**
- Saves position only when detached (`_isAttached == false`)
- When attached: clears saved position (position follows PlaylistWindow)
- Settings keys: `VisWindowLeft`, `VisWindowTop`
- Restoring saved position implies detached state

**Settings persistence:**
- All settings saved to `%APPDATA%/DeadEditor/settings.json`
- Uses existing `LibrarySettings.Save()` infrastructure
- JSON serialization via Newtonsoft.Json
