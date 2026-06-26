using System;
using System.Threading.Tasks;
using DeadEditor.Services;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Shared cold-load gate for the concert reference cache.
    ///
    /// The first concert-database access in a session forces the lazy ~2,300-file load
    /// (<see cref="ConcertLookupService.Instance"/> at its <c>Lazy&lt;T&gt;.Value</c>), which would
    /// otherwise freeze the UI thread for several seconds with no feedback. Every UI entry point that
    /// touches the cache synchronously (Concerts grid, Import "Read" -> Match Setlist, box-set Pull,
    /// Edit Metadata open) awaits this first, so the load runs once, off the UI thread, behind the
    /// modal status overlay. Once warm it is a no-op.
    ///
    /// This is the single home for the idiom EditMetadata first established inline; the status/subtitle
    /// strings and the graceful-degradation error handling are preserved verbatim so the cold-open UX
    /// is identical across every caller.
    /// </summary>
    public static class ConcertDataGate
    {
        /// <summary>
        /// Shows the "Loading concert database…" status overlay only on a cold cache, pre-warming it on
        /// a background thread so the overlay can paint; no-op once warm. Failures are swallowed and
        /// surfaced on the alert banner (the cache degrades gracefully to empty/disabled-setlist state),
        /// so this never throws — safe to await from an <c>async void</c> handler.
        /// </summary>
        public static async Task EnsureLoadedAsync()
        {
            // IsLoaded reads the Lazy's IsValueCreated WITHOUT forcing the load, so the warm path stays
            // on the fast synchronous route and never raises the overlay.
            if (ConcertLookupService.IsLoaded)
                return;

            try
            {
                await App.Alerts.RunWithStatusAsync(
                    "Loading concert database…",
                    // Forcing Instance (the Lazy<T>.Value) inside Task.Run keeps the file load off the UI
                    // thread. Lazy<T> is thread-safe, so concurrent first-callers still load exactly once.
                    _ => Task.Run(() => ConcertLookupService.Instance),
                    "First open this session — just a moment.");
            }
            catch (Exception ex)
            {
                // Don't let the exception escape an async void caller unobserved; surface it on the
                // banner. Callers continue with a cold/degraded cache (GetSetlist returns null ->
                // disabled Match Setlist; the Concerts grid renders empty) rather than crashing.
                App.Alerts.Notify($"Could not load concert data: {ex.Message}",
                    AlertSeverity.Error, "Concert Data");
            }
        }
    }
}
