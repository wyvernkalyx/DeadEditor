using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    /// <summary>
    /// Reads, writes, lists, and deletes <see cref="BoxSetDefinition"/> curation files.
    /// One JSON file per box set under %APPDATA%/DeadEditor/box-sets/, following the
    /// live-write convention concerts use (the directory is created on first save, not
    /// on read). JSON uses the camelCase conventions from <c>ManifestService</c>; writes
    /// use the atomic temp-and-rename pattern from <c>EditSetlistView</c>.
    /// </summary>
    public class BoxSetService
    {
        /// <summary>The AppData box-sets directory. User-created definitions live here so
        /// they survive upgrades and reinstalls.</summary>
        public static string AppDataBoxSetsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeadEditor", "box-sets");

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        /// <summary>
        /// Reads the definition for the given slug, or returns null if the file does not
        /// exist or cannot be parsed (logged, not thrown).
        /// </summary>
        public BoxSetDefinition? Read(string slug)
        {
            var path = Path.Combine(AppDataBoxSetsPath, slug + ".json");

            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                var jObject = JObject.Parse(json);
                return jObject.ToObject<BoxSetDefinition>(JsonSerializer.Create(_jsonSettings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BOXSET] Error reading {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Enumerates every <c>*.json</c> in the box-sets directory and returns the ones
        /// that parse successfully. Returns an empty list if the directory does not exist;
        /// does not create it (reads never create the directory — only writes do).
        /// </summary>
        public IReadOnlyList<BoxSetDefinition> List()
        {
            var result = new List<BoxSetDefinition>();

            if (!Directory.Exists(AppDataBoxSetsPath))
                return result;

            var files = Directory.GetFiles(AppDataBoxSetsPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var jObject = JObject.Parse(json);
                    var definition = jObject.ToObject<BoxSetDefinition>(JsonSerializer.Create(_jsonSettings));
                    if (definition != null)
                        result.Add(definition);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BOXSET] Error reading {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Writes the definition to <c>&lt;slug&gt;.json</c> in the box-sets directory.
        /// Creates the directory if absent (idempotent) and writes atomically via a temp
        /// file + rename. The slug is supplied by the caller so the filename is the
        /// caller's choice (see <see cref="DeriveSlug"/>). Synchronous — callers wrap in
        /// Task.Run if off-UI behavior is needed.
        /// </summary>
        public void Write(BoxSetDefinition definition, string slug)
        {
            Directory.CreateDirectory(AppDataBoxSetsPath);

            var targetPath = Path.Combine(AppDataBoxSetsPath, slug + ".json");
            var tempPath = targetPath + ".tmp";

            var json = JsonConvert.SerializeObject(definition, _jsonSettings);

            File.WriteAllText(tempPath, json);
            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);

            Debug.WriteLine($"[BOXSET] Written: {targetPath}");
        }

        /// <summary>
        /// Deletes the definition file for the given slug. Returns true if a file was
        /// removed, false if it did not exist or could not be deleted (logged, not thrown).
        /// </summary>
        public bool Delete(string slug)
        {
            var path = Path.Combine(AppDataBoxSetsPath, slug + ".json");

            try
            {
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BOXSET] Error deleting {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Derives a deterministic filename slug from a box-set name. Algorithm:
        /// <list type="number">
        ///   <item>Lowercase the input.</item>
        ///   <item>Replace any character that isn't <c>a-z</c>, <c>0-9</c>, or hyphen with a hyphen.</item>
        ///   <item>Collapse consecutive hyphens to one.</item>
        ///   <item>Trim leading/trailing hyphens.</item>
        ///   <item>If the result is empty, return <c>"untitled"</c>.</item>
        /// </list>
        /// Example: <c>"Listen to the River: St. Louis '71 '72 '73"</c> →
        /// <c>"listen-to-the-river-st-louis-71-72-73"</c>.
        /// </summary>
        public static string DeriveSlug(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "untitled";

            var lower = name.ToLowerInvariant();

            var sb = new StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-')
                    sb.Append(ch);
                else
                    sb.Append('-');
            }

            var collapsed = Regex.Replace(sb.ToString(), "-+", "-");
            var trimmed = collapsed.Trim('-');

            return trimmed.Length == 0 ? "untitled" : trimmed;
        }
    }
}
