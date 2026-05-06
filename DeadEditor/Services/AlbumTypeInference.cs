using DeadEditor.Models;

namespace DeadEditor.Services
{
    public static class AlbumTypeInference
    {
        public static AlbumType Infer(AlbumInfo? albumInfo)
        {
            if (albumInfo == null) return AlbumType.AudienceRecording;

            bool hasDate = !string.IsNullOrEmpty(albumInfo.AlbumDate);
            bool hasVenue = !string.IsNullOrEmpty(albumInfo.Venue);
            bool hasAlbumName = !string.IsNullOrEmpty(albumInfo.AlbumName);

            if (hasAlbumName && !hasVenue)
                return AlbumType.OfficialRelease;
            if (hasDate && hasVenue && !hasAlbumName)
                return AlbumType.AudienceRecording;
            if (hasDate && hasVenue && hasAlbumName)
                return AlbumType.OfficialRelease;

            return AlbumType.AudienceRecording;
        }
    }
}
