using System;
using System.Collections.Generic;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure "first .txt wins" info-file resolver (reference-side-panel-spec.md §8). Extracted as a
    /// disk-free seam so the Edit-side View Info affordance can be unit-tested without a folder.
    ///
    /// Mirrors the Import path's behavior exactly: <c>MetadataService.ReadAlbumInfo</c> takes
    /// <c>Directory.GetFiles(folder, "*.txt")[0]</c>, i.e. the first <c>.txt</c> in the enumeration
    /// order. Callers pass the folder listing (any order the filesystem returns); this returns the
    /// first path whose extension is <c>.txt</c> (case-insensitive), or <c>null</c> when none exists.
    /// Multi-<c>.txt</c> disambiguation stays banked (§13).
    /// </summary>
    public static class InfoFileResolver
    {
        public static string? ResolveFirstTextFile(IEnumerable<string>? filePaths)
        {
            if (filePaths == null)
                return null;

            foreach (var path in filePaths)
            {
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
