using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DeadEditor.Services
{
    /// <summary>
    /// Defensive containment check for the managed-library write surface. Source files
    /// are read-only; every legitimate tag-write in DeadEditor operates on a managed
    /// copy under <see cref="LibrarySettings.LibraryRootPath"/>. This guard exists so a
    /// regression that wires a write service to a non-managed path throws at the call
    /// site instead of silently corrupting the user's source archive.
    /// </summary>
    public static class PathGuard
    {
        // Async-local so parallel test classes don't trample each other.
        private static readonly AsyncLocal<string?> _libraryRootOverride = new();

        /// <summary>
        /// Test-only seam: temporarily override the library root used by the
        /// no-root <c>EnsureWithinLibrary</c> overloads. Returns an
        /// <see cref="IDisposable"/> that restores the previous value on dispose.
        /// Production code should never call this.
        /// </summary>
        public static IDisposable OverrideLibraryRootForTesting(string libraryRoot)
        {
            var previous = _libraryRootOverride.Value;
            _libraryRootOverride.Value = libraryRoot;
            return new OverrideScope(previous);
        }

        private sealed class OverrideScope : IDisposable
        {
            private readonly string? _previous;
            public OverrideScope(string? previous) { _previous = previous; }
            public void Dispose() { _libraryRootOverride.Value = _previous; }
        }

        private static string? CurrentLibraryRoot()
            => _libraryRootOverride.Value ?? LibrarySettings.Load().LibraryRootPath;

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> if any of the given paths is
        /// outside the managed library root. Loads the root from
        /// <c>LibrarySettings.Load().LibraryRootPath</c> (or the test override).
        /// </summary>
        public static void EnsureWithinLibrary(IEnumerable<string> paths, string serviceName)
            => EnsureWithinRoot(CurrentLibraryRoot(), paths, serviceName);

        /// <summary>
        /// Single-path overload. See <see cref="EnsureWithinLibrary(IEnumerable{string}, string)"/>.
        /// </summary>
        public static void EnsureWithinLibrary(string path, string serviceName)
            => EnsureWithinRoot(CurrentLibraryRoot(), path, serviceName);

        /// <summary>
        /// Explicit-root overload. The library root is passed in rather than loaded
        /// from settings — exposed for tests so the guard can be exercised without
        /// mutating <c>%APPDATA%</c>. Production code should call
        /// <see cref="EnsureWithinLibrary(IEnumerable{string}, string)"/> instead.
        /// </summary>
        public static void EnsureWithinRoot(string? libraryRoot, IEnumerable<string> paths, string serviceName)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot))
            {
                throw new InvalidOperationException(
                    $"{serviceName}: Library root not configured; refusing to write.");
            }

            var normalizedRoot = NormalizeDirectory(libraryRoot);
            var offenders = new List<string>();

            foreach (var p in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(p) || !IsUnderRoot(normalizedRoot, p))
                {
                    offenders.Add(p ?? "<null>");
                }
            }

            if (offenders.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{serviceName}: refusing to write — path(s) outside managed library root '{libraryRoot}':"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, offenders.Select(p => "  " + p)));
            }
        }

        /// <summary>
        /// Single-path overload. See <see cref="EnsureWithinRoot(string, IEnumerable{string}, string)"/>.
        /// </summary>
        public static void EnsureWithinRoot(string? libraryRoot, string path, string serviceName)
            => EnsureWithinRoot(libraryRoot, new[] { path }, serviceName);

        // Append a trailing separator so a sibling directory whose name starts with the
        // root's name (e.g. root "C:\Lib", candidate "C:\Lib2\foo") does not match.
        private static string NormalizeDirectory(string path)
        {
            var full = Path.GetFullPath(path);
            if (!full.EndsWith(Path.DirectorySeparatorChar) && !full.EndsWith(Path.AltDirectorySeparatorChar))
            {
                full += Path.DirectorySeparatorChar;
            }
            return full;
        }

        private static bool IsUnderRoot(string normalizedRoot, string candidatePath)
        {
            string fullCandidate;
            try
            {
                fullCandidate = Path.GetFullPath(candidatePath);
            }
            catch
            {
                return false;
            }

            return fullCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
