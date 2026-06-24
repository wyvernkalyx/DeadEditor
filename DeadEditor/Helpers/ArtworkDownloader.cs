using System.Net.Http;
using System.Threading.Tasks;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Neutral, MB-free artwork download seam: fetches the raw bytes at a URL.
    /// Extracted from ImportView's MusicBrainz apply path so the generic
    /// "URL -> bytes" unit survives MusicBrainz removal and can back a future
    /// "find artwork" feature. Mime-type handling stays with the caller.
    /// </summary>
    public static class ArtworkDownloader
    {
        /// <summary>
        /// Downloads the bytes at <paramref name="url"/>. Returns null on any
        /// failure (swallowed) — artwork is value-add, never critical.
        /// </summary>
        public static async Task<byte[]?> DownloadAsync(string url)
        {
            try
            {
                using var httpClient = new HttpClient();
                return await httpClient.GetByteArrayAsync(url);
            }
            catch
            {
                // Artwork download failed — not critical
                return null;
            }
        }
    }
}
