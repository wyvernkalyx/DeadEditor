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
    ///
    /// Two locations are involved (mirroring the concerts model — see
    /// <c>ConcertLookupService</c>):
    /// <list type="bullet">
    ///   <item><c>Data/box-sets/</c> (project-relative) — the bundled location shipped
    ///   with the app, copied to the build output via the csproj.</item>
    ///   <item><c>%APPDATA%/DeadEditor/box-sets/</c> — the user-runtime location. On
    ///   first launch, bundled files are copied here; all subsequent reads and writes
    ///   target this location.</item>
    /// </list>
    /// When the <c>DEADEDITOR_DEV</c> environment variable is set to <c>"1"</c>, AppData
    /// is bypassed entirely: reads and writes go straight to <c>Data/box-sets/</c>. This
    /// lets the maintainer's wizard write definitions that are immediately ready to commit.
    /// Distributed users never set this variable.
    ///
    /// JSON uses the camelCase conventions from <c>ManifestService</c>; writes use the
    /// atomic temp-and-rename pattern from <c>EditSetlistView</c>.
    /// </summary>
    public class BoxSetService
    {
        // Build-output bundled path: bin/<config>/<tfm>/Data/box-sets/. In distributed
        // mode this is the first-run seed for AppData. In dev mode, it's the fallback
        // when no repo root can be found by walking up.
        private static readonly string _buildOutputBoxSetsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "box-sets");

        // Lazy so the repo-walk only fires once and only when BundledBoxSetsPath is first
        // accessed. In dev mode the lookup walks the filesystem (cheap); in distributed
        // mode it short-circuits to the build-output path.
        private static readonly Lazy<string> _bundledBoxSetsPath = new(() =>
        {
            if (IsDevMode)
            {
                var repoSourcePath = TryResolveRepoSourceDataPath();
                if (repoSourcePath != null) return repoSourcePath;
            }
            return _buildOutputBoxSetsPath;
        });

        /// <summary>
        /// The bundled box-set definitions location. In distributed mode (the default),
        /// this is the read-only directory next to the running exe used as the first-run
        /// seed for AppData. In dev mode (<c>DEADEDITOR_DEV=1</c>), this resolves to the
        /// repo-source <c>DeadEditor/Data/box-sets/</c> directory if running from a
        /// checkout, so wizard writes land directly in the repo and are immediately visible
        /// to <c>git status</c>. Falls back to the build-output path if no repo root can
        /// be located.
        /// </summary>
        public static string BundledBoxSetsPath => _bundledBoxSetsPath.Value;

        /// <summary>
        /// In dev mode, resolves the repo-source <c>Data/box-sets/</c> directory by walking
        /// up from the running exe's <see cref="AppDomain.CurrentDomain.BaseDirectory"/>
        /// looking for a repository marker (a directory containing a <c>.sln</c> file).
        /// Returns null if no such root can be found, in which case the caller should fall
        /// back to the build-output path. Only called in dev mode; in distributed mode the
        /// AppData path is used and this is irrelevant.
        /// </summary>
        private static string? TryResolveRepoSourceDataPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0)
                {
                    var candidate = Path.Combine(dir.FullName, "DeadEditor", "Data", "box-sets");
                    if (Directory.Exists(candidate))
                    {
                        System.Diagnostics.Debug.WriteLine($"[BoxSetService] Dev mode resolved to repo source: {candidate}");
                        return candidate;
                    }
                    // sln found but the expected project layout isn't there — fail closed.
                    return null;
                }
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>The user-runtime box-sets directory. Populated from the bundle on first
        /// launch; persists user-created definitions across upgrades and reinstalls.
        /// Bypassed entirely when <see cref="IsDevMode"/> is true.</summary>
        public static string AppDataBoxSetsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeadEditor", "box-sets");

        /// <summary>True when the <c>DEADEDITOR_DEV</c> environment variable is set to <c>"1"</c>.
        /// Read on every access, so toggling between runs takes effect on next launch.</summary>
        private static bool IsDevMode =>
            string.Equals(Environment.GetEnvironmentVariable("DEADEDITOR_DEV"), "1", StringComparison.Ordinal);

        /// <summary>The directory all reads and writes target in the current mode.</summary>
        private static string ActiveBoxSetsPath => IsDevMode ? BundledBoxSetsPath : AppDataBoxSetsPath;

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        /// <summary>
        /// Serializes a definition to the canonical box-set JSON form. The single serializer used
        /// by <see cref="Write"/> AND by the wizard's verification baseline/diff snapshots, so the
        /// persisted bytes, the baseline, and the dirty-check all share one format and cannot drift
        /// (box-set-verification-spec.md refinement 2). Pure — no I/O.
        /// </summary>
        public static string Serialize(BoxSetDefinition definition)
            => JsonConvert.SerializeObject(definition, _jsonSettings);

        private static bool _initialized;
        private static readonly object _initLock = new();

        /// <summary>
        /// In distributed mode, copies bundled definitions to AppData on first call if
        /// AppData has no definitions yet (matches the file-count check in
        /// <c>ConcertLookupService:46-54</c>). Idempotent; no-op in dev mode and after the
        /// first successful run.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (IsDevMode) return;
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    bool appDataHasFiles = Directory.Exists(AppDataBoxSetsPath) &&
                                           Directory.GetFiles(AppDataBoxSetsPath, "*.json").Length > 0;
                    bool bundledHasFiles = Directory.Exists(BundledBoxSetsPath) &&
                                           Directory.GetFiles(BundledBoxSetsPath, "*.json").Length > 0;

                    if (!appDataHasFiles && bundledHasFiles)
                    {
                        CopyBundledToAppData(BundledBoxSetsPath, AppDataBoxSetsPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BOXSET] Initialization error: {ex.Message}");
                }

                _initialized = true;
            }
        }

        /// <summary>
        /// Mirrors <c>ConcertLookupService.CopyBundledToAppData</c> (lines 77-95). Creates
        /// the destination directory, copies every <c>*.json</c> from the source, and
        /// never overwrites an existing destination file.
        /// </summary>
        private static void CopyBundledToAppData(string sourceDir, string destDir)
        {
            try
            {
                Directory.CreateDirectory(destDir);
                var files = Directory.GetFiles(sourceDir, "*.json");
                Debug.WriteLine($"[BOXSET] First run: copying {files.Length} box-set files to {destDir}");

                foreach (var file in files)
                {
                    var destFile = Path.Combine(destDir, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        File.Copy(file, destFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BOXSET] Error copying bundled box sets: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the definition for the given slug, or returns null if the file does not
        /// exist or cannot be parsed (logged, not thrown).
        /// </summary>
        public BoxSetDefinition? Read(string slug)
        {
            EnsureInitialized();

            var path = Path.Combine(ActiveBoxSetsPath, slug + ".json");

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
        /// Enumerates every <c>*.json</c> in the active box-sets directory and returns the
        /// ones that parse successfully. Returns an empty list if the directory does not
        /// exist; does not create it (reads never create the directory — only writes do).
        /// </summary>
        public IReadOnlyList<BoxSetDefinition> List()
        {
            EnsureInitialized();

            var result = new List<BoxSetDefinition>();

            if (!Directory.Exists(ActiveBoxSetsPath))
                return result;

            var files = Directory.GetFiles(ActiveBoxSetsPath, "*.json");
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
        /// Writes the definition to <c>&lt;slug&gt;.json</c> in the active box-sets directory
        /// (Data/box-sets/ in dev mode, %APPDATA%/DeadEditor/box-sets/ otherwise). Creates
        /// the directory if absent (idempotent) and writes atomically via a temp file +
        /// rename. The slug is supplied by the caller so the filename is the caller's choice
        /// (see <see cref="DeriveSlug"/>). Synchronous — callers wrap in Task.Run if off-UI
        /// behavior is needed.
        /// </summary>
        public void Write(BoxSetDefinition definition, string slug)
        {
            EnsureInitialized();

            Directory.CreateDirectory(ActiveBoxSetsPath);

            var targetPath = Path.Combine(ActiveBoxSetsPath, slug + ".json");
            var tempPath = targetPath + ".tmp";

            var json = Serialize(definition);

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
            EnsureInitialized();

            var path = Path.Combine(ActiveBoxSetsPath, slug + ".json");

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
