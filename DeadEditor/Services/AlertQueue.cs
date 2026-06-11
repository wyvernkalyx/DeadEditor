using System.Collections.Generic;

namespace DeadEditor.Services
{
    /// <summary>
    /// Pure, UI-free queue + dismissal policy for the in-window alert banner (alert-system-spec.md
    /// Ruling 1). One alert is visible at a time; additional alerts queue FIFO and surface as the
    /// current one is dismissed. Severity drives the auto-dismiss policy. No WPF and no timers live
    /// here — the banner host owns the visuals and the DispatcherTimer, and drives state changes
    /// through this class so the ordering/policy logic stays testable in isolation.
    /// </summary>
    public sealed class AlertQueue
    {
        private readonly Queue<AlertItem> _pending = new();

        /// <summary>The alert currently displayed, or null when nothing is showing.</summary>
        public AlertItem? Current { get; private set; }

        /// <summary>Number of alerts waiting behind the current one.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Add an alert. If nothing is showing it becomes <see cref="Current"/> and is returned
        /// (the caller renders it). If an alert is already showing, the new one is queued behind it
        /// and null is returned (the caller leaves the current render untouched).
        /// </summary>
        public AlertItem? Enqueue(AlertItem item)
        {
            if (Current == null)
            {
                Current = item;
                return item;
            }

            _pending.Enqueue(item);
            return null;
        }

        /// <summary>
        /// Dismiss the current alert and advance to the next queued one. Returns the newly-current
        /// alert to render, or null when the queue is now empty (the caller hides the banner).
        /// A no-op when nothing is showing.
        /// </summary>
        public AlertItem? Dismiss()
        {
            Current = _pending.Count > 0 ? _pending.Dequeue() : null;
            return Current;
        }

        /// <summary>
        /// Severity-dependent dismissal policy (alert-system-spec.md Ruling 1, refined in slice 1):
        /// Info auto-dismisses on a timer; Warning and Error persist until manually closed so a
        /// refusal is always read.
        /// </summary>
        public static bool ShouldAutoDismiss(AlertSeverity severity) => severity == AlertSeverity.Info;
    }
}
