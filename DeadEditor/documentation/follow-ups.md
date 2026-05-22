# Follow-Ups

A single tracked home for known deferred items, so they survive session
boundaries rather than depending on memory. Keep entries dry and factual —
this is a reference list, not a narrative.

## Known Deferred Items

Issues identified but not yet fixed. Each entry: brief description, where it surfaces, when noticed.

### Alias-learning double-write
- **Symptom:** Import view's right-click "Match to Song" writes the new alias to `songs.json` twice on a single match event.
- **Surfaced:** Commit `1840a44` deduped one such occurrence ("The Monkey and the Engineer" written twice for "Monkey And The Engineer").
- **Impact:** Dirty data in `songs.json`; harmless to matching (lookups tolerate dupes) but accumulates over time.
- **Likely location:** The code path that appends to a song's `Aliases` list after a user matches an unmatched track.

### Settings menu slow to open after launch
- **Symptom:** ~26 seconds to open Settings after app launch.
- **Suspected cause:** UI-blocking startup work, likely concert load.
- **Impact:** Poor first-use experience; settings unreachable during startup window.

### Library view doesn't refresh after database reset in Settings
- **Symptom:** After resetting the database via Settings, the Library view continues to show stale state until the app is closed and reopened.
- **Workaround:** Close and reopen the app.
- **Impact:** Confusing UX; users may assume the reset failed.
