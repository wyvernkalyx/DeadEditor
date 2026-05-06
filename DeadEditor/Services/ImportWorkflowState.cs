using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    /// <summary>
    /// Snapshot of Import view state used to derive the workflow stepper's stages.
    /// Pure inputs — no WPF or service references — so the heuristic logic is
    /// trivially testable.
    /// </summary>
    public sealed class ImportWorkflowInput
    {
        public IReadOnlyList<string> TrackTitles { get; init; } = Array.Empty<string>();

        /// <summary>
        /// True if at least one source file under the loaded folder carried a
        /// MusicBrainz Album ID at folder-load time, OR a MusicBrainz lookup has
        /// since been applied this session. Cached by ImportView; not recomputed
        /// per refresh.
        /// </summary>
        public bool MbidPresent { get; init; }

        public string? CurrentFolderPath { get; init; }
        public string? LibraryRootPath { get; init; }

        /// <summary>
        /// Returns true if the input string is an exact alias-table hit in
        /// <see cref="NormalizationService.GetOfficialTitle"/>. Injected so tests
        /// can avoid loading songs.json.
        /// </summary>
        public Func<string, bool>? IsCanonicalSongName { get; init; }

        /// <summary>
        /// Fraction of titles that must be canonical for the Structure stage to
        /// be considered Completed. Default 0.80.
        /// </summary>
        public double StructureMatchThreshold { get; init; } = 0.80;
    }

    /// <summary>
    /// Derives the five workflow stages (Load, Enrich, Clean, Structure, Import)
    /// from a snapshot of Import view state. Heuristics are intentionally cheap;
    /// false positives (a stage shows Completed early) and false negatives (a
    /// stage shows Skipped after the user actually completed it) are both
    /// acceptable — the stepper is signage, not enforcement.
    /// </summary>
    public static class ImportWorkflowState
    {
        // Stage names are constants so tests can assert on them without depending
        // on UI layout.
        public const string LoadName = "Load";
        public const string EnrichName = "Enrich";
        public const string CleanName = "Clean";
        public const string StructureName = "Structure";
        public const string ImportName = "Import";

        // Comma followed by a slash-formatted date — e.g. "Eyes of the World, 5/8/77".
        private static readonly Regex CommaDateRegex =
            new(@",\s*\d{1,2}/\d{1,2}/\d{2,4}", RegexOptions.Compiled);

        // Parenthesized date in any common form — covers (yyyy-MM-dd), (M/D/YY),
        // and (... yyyy ...) venue/date suffixes that NormalizationService would
        // strip. Deliberately broad: false positives are fine.
        private static readonly Regex DateInParenRegex =
            new(@"\((?:[^)]*\d{4}[^)]*|\d{1,2}/\d{1,2}/\d{2,4})\)", RegexOptions.Compiled);

        public static List<WorkflowStage> Compute(ImportWorkflowInput input)
        {
            var stages = new List<WorkflowStage>
            {
                new() { Name = LoadName },
                new() { Name = EnrichName },
                new() { Name = CleanName },
                new() { Name = StructureName },
                new() { Name = ImportName },
            };

            bool loadDone = input.TrackTitles.Count > 0;

            stages[0].IsCompleted = loadDone;
            stages[1].IsCompleted = loadDone && input.MbidPresent;
            stages[2].IsCompleted = loadDone && AreTitlesClean(input.TrackTitles);
            stages[3].IsCompleted = loadDone && AreTitlesCanonical(
                input.TrackTitles,
                input.IsCanonicalSongName,
                input.StructureMatchThreshold);
            stages[4].IsCompleted = loadDone && PathGuard.IsPathUnderRoot(
                input.LibraryRootPath,
                input.CurrentFolderPath);

            DeriveCurrentAndSkipped(stages);
            return stages;
        }

        private static bool AreTitlesClean(IReadOnlyList<string> titles)
        {
            foreach (var title in titles)
            {
                if (string.IsNullOrEmpty(title)) continue;
                if (CommaDateRegex.IsMatch(title)) return false;
                if (DateInParenRegex.IsMatch(title)) return false;
            }
            return true;
        }

        private static bool AreTitlesCanonical(
            IReadOnlyList<string> titles,
            Func<string, bool>? isCanonical,
            double threshold)
        {
            if (isCanonical == null || titles.Count == 0) return false;

            int canonical = 0;
            foreach (var title in titles)
            {
                if (string.IsNullOrEmpty(title)) continue;
                if (isCanonical(title)) canonical++;
            }

            return (double)canonical / titles.Count >= threshold;
        }

        // Find the highest-indexed Completed stage. Earlier non-Completed stages
        // are Skipped (the user moved past them without satisfying their
        // heuristic); the stage immediately after the highest Completed is
        // Current; later stages are Upcoming. If no stage is Completed, Load is
        // Current. If all are Completed, no stage is Current.
        private static void DeriveCurrentAndSkipped(List<WorkflowStage> stages)
        {
            int lastCompletedIndex = -1;
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i].IsCompleted) lastCompletedIndex = i;
            }

            int currentIndex = lastCompletedIndex + 1;
            if (currentIndex >= stages.Count) currentIndex = -1;

            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i].IsCompleted) continue;

                if (i < lastCompletedIndex)
                    stages[i].IsSkipped = true;
                else if (i == currentIndex)
                    stages[i].IsCurrent = true;
                // else: Upcoming (default — all flags false)
            }
        }
    }
}
