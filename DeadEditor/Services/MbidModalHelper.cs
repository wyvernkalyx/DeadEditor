using System.Collections.Generic;
using System.Linq;

namespace DeadEditor.Services
{
    public sealed record MbidModalResult(string? ModalMbid, bool AllAgree, bool AnyPresent);

    public static class MbidModalHelper
    {
        public static MbidModalResult ComputeModal(IReadOnlyList<string?> values)
        {
            var presents = new List<string>(values.Count);
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    presents.Add(v.Trim().ToLowerInvariant());
            }

            if (presents.Count == 0)
                return new MbidModalResult(ModalMbid: null, AllAgree: true, AnyPresent: false);

            var groups = presents.GroupBy(p => p)
                                 .OrderByDescending(g => g.Count())
                                 .ToList();

            return new MbidModalResult(
                ModalMbid: groups[0].Key,
                AllAgree: groups.Count == 1,
                AnyPresent: true);
        }

        public static string? ReadMbidFromFile(string filePath)
        {
            using var file = TagLib.File.Create(filePath, TagLib.ReadStyle.PictureLazy);

            if (file is TagLib.Flac.File flacFile)
            {
                var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
                return xiph?.GetFirstField("MUSICBRAINZ_ALBUMID");
            }

            var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2);
            if (id3v2 != null)
            {
                var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", false);
                if (frame != null && frame.Text.Length > 0)
                    return frame.Text[0];
            }

            return null;
        }
    }
}
