using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free source of the ALBUM / RELEASE field tooltip text, shared by ImportView and
    /// EditMetadataView so both surfaces stay in lockstep. For official releases the field names a
    /// real album and drives releases.json autocomplete; for audience recordings it carries only a
    /// source differentiator (the Date - Venue - City composite is already auto-composed), so the
    /// hint reflects that instead. ToolTip is the app's established field-hint convention (see the
    /// DATE / CITY, STATE fields); no watermark infrastructure exists.
    /// </summary>
    public static class AlbumFieldHint
    {
        public const string Official = "Optional — type to search known releases";
        public const string Audience = "Source / taper (optional) — e.g. Matrix, SBD - Cantor";

        /// <summary>
        /// Returns the audience hint only for <see cref="AlbumType.AudienceRecording"/>; every
        /// other case (official, or a null/unknown type) falls back to the official/release hint,
        /// matching the pre-existing default behavior of both views' album field.
        /// </summary>
        public static string ForType(AlbumType? type) =>
            type == AlbumType.AudienceRecording ? Audience : Official;
    }
}
