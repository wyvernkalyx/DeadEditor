# Title Structure Parser — Specification

**Service:** `DeadEditor.Services.TitleStructureParser`
**Status:** Implemented and integrated. Y2K-2c (0b9fa77), Y2K-2d (5f65376), and Y2K-2e (d53c6f7) shipped on origin/main.
**Last updated:** 2026-04-29 (commit Y2K-2b)

---

## 1. Overview & rationale

Track titles arriving from tags, MusicBrainz, taper sources, and file-name fallbacks are noisy. The legacy code in `MetadataService.ParseTitleAndDate` (PATTERN 1A through 3) and the strip stack inside `NormalizationService.Normalize` (S1–S13) handle this with positional regexes — strip the trailing parenthetical that matches a known shape, then strip the next one, etc. That approach has two problems:

1. **It mis-strips canonical-paren song titles.** Real songs contain parentheses as part of the canonical name — `Caution (Do Not Stop on Tracks)`, `Ain't It Crazy (The Rub)`, `The Stranger (Two Souls in Communion)`, and four others. A strip-by-position parser can't tell these apart from `(1969-12-26)`.
2. **It doesn't handle venue-first parens.** Shapes like `Cold Rain And Snow (San Francisco, 11/2/69)` — venue text first, date second — aren't matched by any existing PATTERN, so the date never gets extracted.

`TitleStructureParser` replaces the position-based strip with a **content-based classifier**. Each `(...)` or `[...]` group is examined for metadata signals (date pattern, US/Canadian state code, "Live at"/"Filler:"/"Remaster"/etc.) and only stripped if at least one signal fires. Canonical parens have no signal and survive unchanged. Venue-first metadata parens are recognized by their date or state code regardless of where the venue text sits within the group.

Commit Y2K-2b introduced the parser as a **standalone service**. It is now wired into production: `MetadataService.ParseTitleAndDate` (Y2K-2c) and `NormalizationService.Normalize` (Y2K-2d) both delegate to it, and the legacy dead code was retired (Y2K-2e).

---

## 2. Public API

```csharp
namespace DeadEditor.Services;

public sealed record TitleParseResult(
    string SongName,
    bool HasSegue,
    string? TrackDate,
    string? Venue,
    IReadOnlyList<string> RawMetadataFragments);

public static class TitleStructureParser
{
    public static TitleParseResult Parse(string? title, string? albumDate = null);
}
```

### Result fields

| Field | Type | Meaning |
|---|---|---|
| `SongName` | `string` | Cleaned song name with canonical parens preserved. Empty for null/whitespace input. Never `null`. |
| `HasSegue` | `bool` | `true` if any segue marker (`>`, `→`, `->`, `–>`, `[>]`) was detected anywhere in the input. |
| `TrackDate` | `string?` | `yyyy-MM-dd` if a valid date was extracted; `null` otherwise. |
| `Venue` | `string?` | Venue text from the first metadata fragment that yielded venue content; `null` otherwise. May include a trailing state code (e.g., `"the Capitol Theatre, Port Chester, NY"`). |
| `RawMetadataFragments` | `IReadOnlyList<string>` | Raw inner-text of every group classified as metadata, in left-to-right order. Empty list (never `null`) if no metadata was found. |

### Stateless, no exceptions

- Static class. No instance state. Thread-safe by construction.
- Null input, empty string, and whitespace-only input all return `new TitleParseResult("", false, null, null, Array.Empty<string>())` — no exception thrown.
- `albumDate` is consulted only by the two-digit-year resolution path; null disables the album-century rule and falls back to the pivot rule (see `TwoDigitYearResolver`).

---

## 3. Pipeline

The parser executes seven steps in fixed order. Each step transforms a working copy of the input string.

| # | Step | Purpose |
|---|---|---|
| 1 | **Cosmetic prep** | Replace `//` (tape splice marker) with a space. Normalize curly apostrophes (U+2018, U+2019) and backtick to U+0027. |
| 2 | **Strip artist-suffix tail** | Regex `\s+-\s+[^_]+__\s*$` removes MusicBrainz API tails like ` - Grateful Dead__`. |
| 3 | **Strip year-tour suffix** | Regex `\s*\((\d{4})\s+[-–—]\s+[^)]+\)\s*$` removes `(1972 - Europe '72)` and similar. Whitespace is required on both sides of the dash so ISO dates like `(1969-12-26)` are not misclassified as year-tour suffixes. Year alone is not a track date. |
| 4 | **Walk parens & brackets** | Each `(...)` or `[...]` group is classified as metadata or content (see §4). Metadata groups are stripped from the working copy and recorded in `RawMetadataFragments`. Content groups are preserved verbatim. Bracket groups whose entire content is `>` are skipped — the segue handler handles them. |
| 5 | **Detect & strip segue markers** | Trailing markers (anchored to end-of-string) are stripped. Embedded markers anywhere in the working copy set `HasSegue=true` but are NOT stripped. See §6. |
| 6 | **Normalize remaining dashes** | Replace en-dash (U+2013), em-dash (U+2014), and box-drawing horizontal (U+2500) with ASCII hyphen. Done after segue detection because en-dash is part of one segue variant. |
| 7 | **Collapse whitespace, trim** | Multiple internal spaces become one; outer whitespace is trimmed. The result is `SongName`. |

Order matters. Year-tour suffix is stripped before the paren walker so canonical parens earlier in the title aren't confused with the year-tour shape. Segue detection runs after the paren walker so a content paren containing `>` (hypothetical) wouldn't be mis-classified as segue, and so `[>]` (which the paren walker skips) is handled in one place.

---

## 4. Classification rules

A `(...)` or `[...]` group is classified as **metadata** if any one of the following signals is present in the inner text. Otherwise it is **content** and preserved verbatim.

| Signal | Pattern |
|---|---|
| ISO date | `\b\d{4}-\d{1,2}-\d{1,2}\b` |
| Year-first slash date | `\b\d{4}/\d{1,2}/\d{1,2}\b` |
| US-style slash date | `\b\d{1,2}/\d{1,2}/\d{2,4}\b` |
| US state code | Two-letter all-uppercase token preceded by `,` (with optional whitespace) **or** the only non-whitespace token in the fragment, matching the 50 US states + DC |
| Canadian province code | Same rule, matching AB, BC, MB, NB, NL, NS, NT, NU, ON, PE, QC, SK, YT |
| "Live at" / "Live in" | `\bLive\s+(at\|in)\b` (case-insensitive) |
| "Filler:" | `\bFiller:` (case-insensitive) |
| "Remaster" / "Remastered" | `\bRemastered?\b` (case-insensitive) |
| "Reprise" | `\bReprise\b` (case-insensitive) |
| Standalone "Live" | Inner text trimmed equals `"Live"` (case-insensitive) |

### State / province list

64 codes total, inline `static readonly HashSet<string>`:

- **US 50 + DC:** AL, AK, AZ, AR, CA, CO, CT, DE, FL, GA, HI, ID, IL, IN, IA, KS, KY, LA, ME, MD, MA, MI, MN, MS, MO, MT, NE, NV, NH, NJ, NM, NY, NC, ND, OH, OK, OR, PA, RI, SC, SD, TN, TX, UT, VT, VA, WA, WV, WI, WY, DC
- **Canadian:** AB, BC, MB, NB, NL, NS, NT, NU, ON, PE, QC, SK, YT

US territories (PR, VI, GU, AS, MP) are excluded. The state-preceded-by-comma rule prevents false positives on hypothetical content like `(IN ESCROW)`.

### Default = content

Critical to canonical-paren preservation. If no signal fires, the group stays in place. Worked examples:

- `(Do Not Stop on Tracks)` → no date, no state, no marker → **content** → preserved.
- `(San Francisco, 11/2/69)` → US-slash date present → **metadata** → stripped.
- `(2020 Remaster)` → "Remaster" marker → **metadata** → stripped.
- `(Two Souls in Communion)` → no signal → **content** → preserved.

---

## 5. Date extraction

When a fragment is classified as metadata, the parser tries date patterns in this order against the inner text:

1. **ISO** — `\b(\d{4})-(\d{1,2})-(\d{1,2})\b`
2. **Year-first slash** — `\b(\d{4})/(\d{1,2})/(\d{1,2})\b`
3. **US slash** — `\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b`. If the year is `< 100`, route through `TwoDigitYearResolver.ResolveTwoDigitYear(year, albumDate)`.

**First match wins.** Order matters: a string like `1971/07/02` must parse as year=1971/month=7/day=2 (year-first slash), not as the substring `71/07/02` via US-slash. Trying patterns in ISO → year-first → US order resolves the ambiguity.

**Validation:** month 1–12, day 1–31, year 1900–2100. Construct `yyyy-MM-dd`. If validation fails, return `null` for the date — but the fragment **stays classified as metadata** (it fired the classification signal, even if the value was invalid). The fragment is still recorded in `RawMetadataFragments` and removed from the working copy.

**Multiple dates within one fragment** (rare): pick the first match and ignore the rest.

**Multiple metadata fragments**: first fragment that yields a parseable date wins. Date and venue can come from different fragments.

---

## 6. Segue detection

Segue markers recognized: `>`, `→` (U+2192), `->` (hyphen-greater), `–>` (en-dash-greater U+2013), `[>]` (bracket-enclosed).

### Trailing markers — stripped

Pattern `(?:\s*(?:[-–]?>|→|\[\s*>\s*\]))+\s*$` is anchored to end-of-string and may repeat (handles ` > > >`). When it matches, the entire trailing run is stripped and `HasSegue` is set to `true`.

### Embedded markers — preserved, `HasSegue` still set

Markers anywhere in the working copy other than at the trailing position set `HasSegue=true` but are **not stripped**. The original characters and surrounding text remain in `SongName`. See §7 known limitations for why this is the chosen behavior.

### `[>]` special case

A bracket group whose entire content is `>` (or `\s*>\s*`) is treated as a segue marker, not as a paren-bracket group:

- The paren walker (step 4) **skips** such bracket groups, leaving them in place.
- The segue handler (step 5) either strips them (if trailing) or preserves them (if embedded), and sets `HasSegue=true` either way.

This concentrates segue handling in one place.

---

## 7. Venue extraction

For each fragment classified as metadata, after date extraction, the parser produces venue text:

1. Remove the matched date substring (the exact characters that produced `TrackDate`). If date validation rejected the value, fall back to stripping any date-shaped substring.
2. Strip `Live at ` / `Live in ` / `Filler:` prefix tokens (case-insensitive).
3. Strip standalone `Remastered`, `Remaster`, `Reprise`, `Live` words (case-insensitive).
4. Strip orphan 4-digit year tokens (handles `(2020 Remaster)` → `2020 ` → `""`).
5. Collapse internal whitespace, trim outer whitespace + commas + dashes + periods.
6. Empty result → `Venue = null`. Otherwise → that's the venue.

**State codes are kept** in the venue string. Future display code may want them. Worked examples:

| Inner fragment | TrackDate | Venue |
|---|---|---|
| `San Francisco, 11/2/69` | `1969-11-02` | `San Francisco` |
| `Boston, MA, 11/2/69` | `1969-11-02` | `Boston, MA` |
| `Live at the Capitol Theatre, Port Chester, NY 2/21/1971` | `1971-02-21` | `the Capitol Theatre, Port Chester, NY` |
| `Filler: 1972-05-04 - Some Venue` | `1972-05-04` | `Some Venue` |
| `2020 Remaster` | `null` | `null` |
| `Reprise` | `null` | `null` |
| `Live` | `null` | `null` |
| `Boston, MA` | `null` | `Boston, MA` |
| `13/45/69` (invalid) | `null` | `null` |

**Multiple metadata fragments:** first fragment that yields non-empty venue wins. Date and venue can come from different fragments.

---

## 8. Known limitations

- **Embedded segue markers preserved in `SongName`.** When a track title contains a segue marker mid-string (e.g., `"Dark Star > St. Stephen"`), the marker and the second song name are preserved in `SongName`. `HasSegue` is set to `true`. This is a data-preservation choice — the canonical fix is to split such tracks into two `TrackInfo` instances (one with `HasSegue=true` and `SongName="Dark Star"`, one with `SongName="St. Stephen"`), but track-splitting requires data-model changes outside this commit's scope. Until splitting is implemented, the original title is preserved unchanged so future work can recover both song names without data loss. Match Setlist and alias lookup will fail on these titles; users can manually correct via the song-matching UI.

- **Canonical-paren detection relies on absence of signals.** The seven canonical-paren song names in `Data/songs.json` survive because none of them contain a date pattern, state code, or known marker. If a future canonical title were to include one (e.g., a song literally named `Boston, MA`), the parser would mis-classify it as metadata. No current canonical title triggers this.

- **Day-first European date format treated as M/D/YY.** A title containing `(13/2/71)` would parse the leading `13` as month, fail validation (month 13 invalid), and return `null` for `TrackDate`. The fragment is still classified as metadata and stripped. There is no day-first parsing path.

- **`>` inside a content paren — hypothetical false positive.** If a future canonical paren happened to contain `>`, the embedded-segue detection would set `HasSegue=true`. No current canonical title is shaped this way. (One alias in `Data/songs.json` — `"I Know You Rider> High Time tease"` — does contain `>`, but it's a taper-notation alias filed for future cleanup, not a canonical title.)

- **Nested parens / brackets not supported.** The paren walker uses `[^()]*` and `[^\[\]]*` inside the group regexes — nested groups are not matched. No real-world title is known to need them.

- **Year-tour suffix is end-anchored.** A title like `Foo (1972 - Europe '72) (1970)` would not strip the year-tour fragment because it isn't at the end. The trailing `(1970)` falls through the paren walker as content (year alone, no signal). Acceptable: no current title has this shape.

- **Artist-suffix-tail strip requires `__`.** The MB API consistently appends `__`. A title like `Foo - Bar` (without underscores) is left alone. This is intentional to avoid stripping legitimate ` - text` suffixes.

---

## 9. Relationship to `TwoDigitYearResolver`

All two-digit-year resolution delegates to `TwoDigitYearResolver.ResolveTwoDigitYear` from commit Y2K-1. The parser passes `albumDate` through unchanged, so the resolver's two rules (album-century preference within ±1, otherwise pivot at `currentYear+5`) apply identically here. The parser owns no Y2K logic of its own.

See `documentation/12-normalization-service.md` § Two-Digit Year Resolution for the resolver's behavior.

---

## 10. Migration plan (high-level)

Three subsequent commits integrated the parser:

- **Y2K-2c** (`0b9fa77`) — `MetadataService.ParseTitleAndDate` was rewritten to delegate to `TitleStructureParser.Parse`. PATTERNS 1A through 3 retired. The existing `(songName, hasSegue, date)` tuple shape is preserved at the call site; the new `Venue` and `RawMetadataFragments` fields are not yet plumbed through.
- **Y2K-2d** (`5f65376`) — `NormalizationService.Normalize`'s strip stack (S1–S13) was replaced by a call into the parser. Pre-lookup transforms (cosmetic, dash normalization) moved into the parser path or stayed in Normalize as appropriate.
- **Y2K-2e** (`d53c6f7`) — Dead code removed (PATTERN 1A–3, S1–S13 helpers), call-site tidying, and documentation cleanup.

Each commit is independently testable. The parser's standalone status (this commit) means we can verify its correctness with the test suite alone before any caller changes.
