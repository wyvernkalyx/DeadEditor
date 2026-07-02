using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free composer for the library grid's "Album Name" column display value
    /// (and album-name search). Kept out of the code-behind so the derivation is
    /// unit-testable in isolation (mirrors the <see cref="BoxSetPullCollision"/> pure-helper
    /// pattern).
    ///
    /// <para>
    /// Official releases carry a real album name, so we surface it verbatim. Audience
    /// recordings have <b>no</b> real album name — their interop ALBUM tag already holds the
    /// composed <c>"Date - Venue - City, ST[ - Source]"</c> (written from
    /// <see cref="AlbumInfo.AlbumTitle"/> at import). This helper reconstructs that same
    /// composite from the row's fields so the grid can show it instead of a blank cell.
    /// </para>
    ///
    /// <para>
    /// <b>Display-only.</b> The value is derived on read; it is never written back to
    /// <see cref="LibraryShow.AlbumName"/>, the ALBUMNAME custom tag, or the manifest, so the
    /// composers that build folder names / ALBUM tags (which would otherwise re-append it and
    /// double up) never see it. The differentiator itself still lives in
    /// <see cref="LibraryShow.AlbumName"/> (from the ALBUMNAME tag) and is folded back in here.
    /// </para>
    /// </summary>
    public static class AlbumDisplayName
    {
        /// <summary>
        /// Composes the grid display album name for a row. Mirrors the audience branch of
        /// <see cref="AlbumInfo.AlbumTitle"/> so the displayed value matches the stored ALBUM tag.
        /// </summary>
        /// <param name="type">The row's album type.</param>
        /// <param name="date">Performance date (yyyy-MM-dd); maps to <c>AlbumDate</c>.</param>
        /// <param name="venue">Venue name.</param>
        /// <param name="location">City, ST; maps to <c>CityState</c>.</param>
        /// <param name="albumName">Real album name (official) or source differentiator (audience).</param>
        public static string Compose(AlbumType type, string? date, string? venue, string? location, string? albumName)
        {
            var name = albumName ?? "";

            // Official releases (studio, live, box sets) carry a real album name — show it as-is.
            if (type == AlbumType.OfficialRelease)
                return name;

            var d = date ?? "";
            var v = venue ?? "";
            var loc = location ?? "";

            // No concert coordinates (date + venue both absent): fall back to whatever
            // differentiator exists, mirroring AlbumTitle's studio-fallback branch for audience.
            if (string.IsNullOrEmpty(d) && string.IsNullOrEmpty(v))
                return name;

            var composite = $"{d} - {v} - {loc}";
            if (!string.IsNullOrEmpty(name))
                composite += $" - {name}";
            return composite;
        }
    }
}
