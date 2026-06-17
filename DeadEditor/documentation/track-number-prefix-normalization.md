# Track-Number Prefix Normalization

Status: helper + tests implemented this commit (Phase B-1). `NormalizationService`
wiring and the `TrackNumber` plumbing are **pending the next layer** — this layer
adds the pure rule and its contract only, with **no behavior change** (no caller).

## Problem

Importing a concert whose track titles carry an embedded number prefix — e.g.
`01 - Bertha`, `02 - Good Lovin'`, `1. Good Lovin'` — sends every track to the
**Unmatched Songs dialog** even though the canonical name (`Bertha`, `Good
Lovin'`) exists in `songs.json`. The dialog then pre-fills the dropdown with the
raw prefixed string instead of the matched canonical name. This is common given
taper/ripper folder- and tag-naming conventions.

## Regression history

The leading-track-number strip used to live in `MetadataService.CleanTitle`
(`Regex.Replace(title, @"^\d+[\s\.\-_]+", "")`, CLAUDE.md "Track Title
Normalization" step 1). `CleanTitle` is now **dead code** — its only call site
was removed (`MetadataService.cs:129`, "REMOVED - this was destroying original
metadata") and no live caller remains. When matching moved to
`TitleStructureParser` (commit Y2K-2d), the leading-track-number strip was **not
carried over**, so the parser's `SongName` output (the comparison key) still
carries the prefix and the canonical lookup misses.

Diagnosis confirmed in Phase A: the failure is purely the missing strip stage;
`TitleStructureParser.Parse` handles tape markers, artist suffix, paren/bracket
metadata, segues, and dashes, but has no track-number-prefix stage. Fuzzy match
cannot recover it — `Levenshtein("01 - Bertha", "Bertha") = 5`, far past the
distance ceiling.

## Decisions (agreed design)

1. **Pure helper.** Implement the strip as a pure, separately-tested
   `TrackNumberPrefix.Strip` helper, following the `DuplicateDateRule` /
   `ConcertVerifyGate` precedent. Lives in `DeadEditor/Services/`, namespace
   `DeadEditor.Services`.
2. **Home = `NormalizationService.Normalize`, match-key-only (Option B).** The
   strip feeds **only the comparison key** used for canonical lookup. The stored
   FLAC tag and the dialog's `RawTitle` are never touched. *(Realized in the next
   layer; recorded here as the agreed design.)*
3. **Matched tracks auto-resolve.** A stripped title that then matches canonical
   is marked matched and **skips** the Unmatched Songs dialog. *(Next layer.)*

## Safety property

Because the strip feeds only the comparison key (Option B), **a false strip
cannot corrupt data.** If the helper strips something it shouldn't, the stripped
key simply fails the canonical lookup and the track stays unmatched; the original
`RawTitle` is preserved and shown in the dialog. The worst case is "did not
auto-resolve," never "mangled title."

## Rules

`Strip(string title, int? trackNumber = null)` removes **at most one** leading
prefix and never alters the song-name remainder (internal hyphens, casing,
spacing preserved). It does no other normalization (case/diacritics/etc. remain
the pipeline's job).

**Structurally unambiguous — strip unconditionally (no `trackNumber` needed):**

- **Digits + separator:** `01 - `, `1. `, `01) `, `01_`, including unicode en-dash
  (`–`) and em-dash (`—`). Stripped regardless of whether the remainder begins with
  a digit, so number-titled songs after a track prefix resolve (e.g. `03 - 46 Days`
  → `46 Days`, `05 - 2120 South Michigan Avenue` → `2120 South Michigan Avenue`,
  `03 - 2001` → `2001` — a real multi-band pattern: Phish, blues standards).
- **Colon separator** (`01: Sugaree`) is **ambiguous with time codes** (`8:05`,
  `12:34`), so the colon counts as a track separator **only when followed by
  whitespace**. `01: Sugaree` strips; `8:05` and `12:34 Jam` pass through unchanged.
- **Disc-track tokens:** `d1t01 `, `s1t05 ` (optional leading disc/source letter),
  and `1-01 ` (digits-hyphen-digits).

**Bare-space form — strip ONLY when gated (the one ambiguous case):**

- Leading digits, whitespace, then the title (`01 Bertha`, `10 Sugaree`). Strip
  iff `trackNumber.HasValue` **and** the leading number equals `trackNumber`
  **or** `trackNumber % 100` (the latter covers disc-encoded numbering such as a
  displayed `Track 101`). Otherwise the title passes through unchanged. Without a
  `trackNumber` there is no gate and the bare-space form is left intact.

## Multi-band negative cases (locked by test)

The app is multi-band by design; titles that legitimately begin with a number
must survive. The old naive `^\d+[\s.\-_]+` regex would corrupt them (`16 Tons`
→ `Tons`). These pass through unchanged when no gate matches:

- `16 Tons`
- `50 Ways to Leave Your Lover`
- `2120 South Michigan Avenue`
- `99 Luftballons`

## Documented residual edges

- **`Strip("16 Tons", 16)` → `"Tons"`.** Rare collision where the leading number
  equals the track number. Acceptable under the match-key-only safety property:
  `Tons` fails the canonical lookup and the original `RawTitle` is preserved —
  graceful, not corruption.
- **Time codes** (`8:05`, `12:34 Jam`) are guarded by the colon-only "followed by
  whitespace" carve-out, so they are not mistaken for a numbered prefix. Other
  separators (`-`, `.`, `)`, `_`, en/em dash) intentionally strip even when the
  remainder is numeric, so number-titled songs after a track prefix resolve.

## Phase A data note

`songs.json` was verified **clean**: no prefix-polluted entries (`01 - Bertha`
style) and no canonical titles beginning with a digit. No data cleanup follow-up
is needed.

## Status / next layer

- This commit: `TrackNumberPrefix` helper + `TrackNumberPrefixTests` contract.
- Pending: wire `TrackNumberPrefix.Strip` into `NormalizationService.Normalize`
  (match-key-only), plumb the track's `TrackNumber` through for the gated
  bare-space form, and auto-resolve stripped+matched tracks so they skip the
  Unmatched Songs dialog.
