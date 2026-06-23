using System;
using System.Threading.Tasks;
using System.Windows;

namespace DeadEditor.Services
{
    /// <summary>
    /// Shell-wide singleton alert service (alert-system-spec.md § Service API). Mirrors the
    /// <c>AudioPlayerService.Instance</c> pattern: one instance reached from any view (via
    /// <c>App.Alerts</c>). It owns no visuals — it forwards notifications to a registered
    /// <see cref="IAlertSink"/> (the shell's banner host) on the UI thread.
    /// </summary>
    public sealed class AlertService : IAlertService
    {
        private static readonly AlertService _instance = new();

        /// <summary>The singleton instance.</summary>
        public static AlertService Instance => _instance;

        private AlertService() { }

        private IAlertSink? _sink;
        private IConfirmHost? _confirmHost;
        private IReadPanelHost? _readPanelHost;
        private IStatusHost? _statusHost;

        // Active-scope count for the please-wait overlay. Overlapping RunWithStatusAsync scopes keep
        // the overlay up until the LAST one completes (so a premature hide cannot strand a still-
        // running operation). Touched only on the UI thread — the entering ++ runs on the caller's
        // UI thread, and the finally -- resumes on the UI thread via the captured SynchronizationContext
        // (production) or directly (no-dispatcher unit tests), so no locking is required.
        private int _activeStatusScopes;

        /// <summary>
        /// Register the visual banner host. Called once by <c>ShellWindow</c> at construction — the
        /// shell is the single window created at startup, so the sink is always attached before any
        /// view can call <see cref="Notify"/>. With no sink attached, Notify is a silent no-op.
        /// </summary>
        public void RegisterSink(IAlertSink sink) => _sink = sink;

        /// <summary>
        /// Register the visual confirm host (the shell's dimmed-scrim overlay). Called once by
        /// <c>ShellWindow</c> at construction, alongside <see cref="RegisterSink"/>. With no host
        /// attached, <see cref="ConfirmAsync"/> resolves to <c>false</c> (the safe/negative answer).
        /// </summary>
        public void RegisterConfirmHost(IConfirmHost host) => _confirmHost = host;

        /// <summary>
        /// Register the visual read-panel host (the shell's dimmed-scrim viewer overlay). Called once
        /// by <c>ShellWindow</c> at construction, alongside the other host registrations. With no host
        /// attached, <see cref="ShowReadPanel"/> is a silent no-op.
        /// </summary>
        public void RegisterReadPanelHost(IReadPanelHost host) => _readPanelHost = host;

        /// <summary>
        /// Register the visual status host (the shell's please-wait dimmed-scrim overlay). Called once
        /// by <c>ShellWindow</c> at construction, alongside the other host registrations. With no host
        /// attached, <see cref="RunWithStatusAsync"/> still runs the work (and refcounts the scope) —
        /// it simply shows no overlay.
        /// </summary>
        public void RegisterStatusHost(IStatusHost host) => _statusHost = host;

        public void Notify(string message, AlertSeverity severity = AlertSeverity.Info, string? title = null)
        {
            var sink = _sink;
            if (sink == null) return;

            var item = new AlertItem(message, severity, title);

            // Marshal to the UI thread; banners may be raised from background continuations.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                sink.Show(item);
            else
                dispatcher.Invoke(() => sink.Show(item));
        }

        public Task<bool> ConfirmAsync(string message, string title,
                                       string confirmLabel = "Yes", string cancelLabel = "No",
                                       bool defaultToConfirm = true)
        {
            // Blocking decisions are raised from UI-thread event handlers (the save/cancel paths);
            // the confirm host renders + focuses a card, so this requires the UI thread. With no host
            // attached, default to false — the negative/safe answer (e.g. "keep editing", "stay").
            var host = _confirmHost;
            if (host == null) return Task.FromResult(false);

            return host.ConfirmAsync(message, title, confirmLabel, cancelLabel, defaultToConfirm);
        }

        public Task<ConfirmResult> ConfirmAsync(string message, string title,
                                                string confirmLabel, string declineLabel, string cancelLabel)
        {
            // Three-way path. With no host attached, default to Cancel — the tri-state safe answer
            // (abort, touch nothing).
            var host = _confirmHost;
            if (host == null) return Task.FromResult(ConfirmResult.Cancel);

            return host.ConfirmAsync(message, title, confirmLabel, declineLabel, cancelLabel);
        }

        public void ShowReadPanel(string title, string content)
        {
            // Opened from a UI-thread click handler (the Import "View Info" button). With no host
            // attached, silently no-op.
            _readPanelHost?.Show(title, content);
        }

        public async Task RunWithStatusAsync(string title, Func<IProgress<StatusUpdate>, Task> work,
                                             string? message = null)
        {
            var host = _statusHost;

            // Enter the scope and show. The active-scope counter (not a bool) means overlapping calls
            // keep the overlay up until the LAST completes; last-write-wins on the displayed title.
            _activeStatusScopes++;
            InvokeOnUi(() => host?.ShowStatus(title, message));

            // Created here (on the UI thread) so UpdateStatus reports marshal back automatically via
            // the captured SynchronizationContext — work may report from a background continuation.
            var progress = new Progress<StatusUpdate>(u => host?.UpdateStatus(u.Message));

            try
            {
                await work(progress);
            }
            finally
            {
                // Guaranteed hide on success OR exception. Only the LAST overlapping scope hides.
                if (--_activeStatusScopes == 0)
                    InvokeOnUi(() => host?.HideStatus());
            }
            // The exception (if any) propagates out of the finally — rethrown so callers can surface
            // a banner if the work failed.
        }

        /// <summary>
        /// Run <paramref name="action"/> on the UI thread, mirroring <see cref="Notify"/>'s marshalling:
        /// invoke directly when already on the dispatcher thread (or in headless unit tests where
        /// there is no <c>Application.Current</c>), otherwise marshal across.
        /// </summary>
        private static void InvokeOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
    }
}
