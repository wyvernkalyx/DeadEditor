using System;
using System.Threading.Tasks;

namespace DeadEditor.Services
{
    /// <summary>
    /// Severity of an in-window alert. Drives the banner color and the dismissal policy
    /// (alert-system-spec.md Ruling 1): Info auto-dismisses; Warning/Error persist until closed.
    /// </summary>
    public enum AlertSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// One alert to display in the banner. Immutable; carries the body, the severity, and an
    /// optional title shown as the banner's bold label (falls back to the severity word).
    /// </summary>
    public sealed record AlertItem(string Message, AlertSeverity Severity, string? Title);

    /// <summary>
    /// Tri-state result of the three-way confirm host (alert-system-spec.md Ruling 2). Role-named
    /// (not Yes/No/Cancel) because the buttons carry caller-supplied labels: <see cref="Confirm"/> is
    /// the primary/affirmative button, <see cref="Decline"/> the secondary alternative, and
    /// <see cref="Cancel"/> the abort (Esc maps here). The names mirror the two-way path's
    /// <c>confirmLabel</c>/<c>cancelLabel</c> vocabulary rather than presuming button text.
    /// </summary>
    public enum ConfirmResult
    {
        Confirm,
        Decline,
        Cancel
    }

    /// <summary>
    /// One mid-operation status report pushed through the <see cref="IProgress{T}"/> the work
    /// delegate receives from <see cref="IAlertService.RunWithStatusAsync"/>. Carries the new
    /// message line to show under the title in the please-wait overlay (alert-system-spec.md
    /// § Status overlay). A record struct because it is a tiny, immutable value passed frequently.
    /// </summary>
    public readonly record struct StatusUpdate(string Message);

    /// <summary>
    /// Shell-wide alert surface (alert-system-spec.md). Slice 1 implements <see cref="Notify"/>
    /// only — the silent in-window banner for bucket-A fire-and-forget notifications.
    /// <c>ConfirmAsync</c> (bucket B) is added in increment 3, not stubbed here.
    /// </summary>
    public interface IAlertService
    {
        /// <summary>
        /// Show a fire-and-forget notification in the in-window banner. Silent (no system chime)
        /// and never steals keyboard focus. Returns immediately; if a banner is already showing
        /// the alert queues behind it.
        /// </summary>
        void Notify(string message, AlertSeverity severity = AlertSeverity.Info, string? title = null);

        /// <summary>
        /// Show a silent, in-window blocking confirmation (bucket B, alert-system-spec.md Ruling 2):
        /// a full-shell dimmed scrim + centered card rendered inside the shell, no OS chrome, no
        /// system chime. Returns the user's decision via a <see cref="TaskCompletionSource{TResult}"/>
        /// so the await resumes on the UI thread, fitting the existing async save/cancel paths.
        /// <para>
        /// Two-way (Yes/No or OK/Cancel) — <c>true</c> = the affirmative button
        /// (<paramref name="confirmLabel"/>) was chosen; <c>false</c> = the negative/safe button
        /// (<paramref name="cancelLabel"/>) OR Esc. <paramref name="defaultToConfirm"/> controls which
        /// button is focused/default (the one Enter activates): <c>true</c> (the default) focuses
        /// Confirm; pass <c>false</c> to focus the negative button, preserving a site that deliberately
        /// defaulted to the safe answer (a Win32 <c>MessageBox</c> with an explicit
        /// <c>MessageBoxResult.No</c> default — e.g. the destructive reset/re-enrich/start-fresh
        /// confirms). Esc always resolves <c>false</c> regardless.
        /// </para>
        /// <remarks>
        /// Re-entrancy: calling this while a confirm is already showing throws
        /// <see cref="System.InvalidOperationException"/> — one blocking decision owns the surface at
        /// a time (the scrim structurally prevents a second user-initiated confirm, so re-entrancy is
        /// a logic error, not a queue case). Must be called on the UI thread.
        /// </remarks>
        /// </summary>
        Task<bool> ConfirmAsync(string message, string title,
                                string confirmLabel = "Yes", string cancelLabel = "No",
                                bool defaultToConfirm = true);

        /// <summary>
        /// Three-way blocking confirm (alert-system-spec.md Ruling 2): a dimmed card with three
        /// buttons. Returns <see cref="ConfirmResult.Confirm"/> for <paramref name="confirmLabel"/>,
        /// <see cref="ConfirmResult.Decline"/> for <paramref name="declineLabel"/>, and
        /// <see cref="ConfirmResult.Cancel"/> for <paramref name="cancelLabel"/>. The default/focused
        /// button is Confirm (Enter); <b>Esc resolves Cancel</b> — the tri-state safe answer (mirrors
        /// a Win32 YesNoCancel box, whose Esc closes to Cancel). Same scrim/card/TCS plumbing and
        /// re-entrancy rule as the two-way path. Must be called on the UI thread.
        /// </summary>
        Task<ConfirmResult> ConfirmAsync(string message, string title,
                                         string confirmLabel, string declineLabel, string cancelLabel);

        /// <summary>
        /// Show a long text document in a silent, in-window scrollable read panel (bucket C,
        /// alert-system-spec.md Ruling 3): a full-shell dimmed scrim + LARGE centered card with a
        /// title, a read-only/selectable monospace text area, and a Close button. Esc and Close both
        /// dismiss. Fire-and-forget — it returns no value (a viewer, not a decision). Must be called
        /// on the UI thread. Used by the Import info-file viewer (#33).
        /// </summary>
        void ShowReadPanel(string title, string content);

        /// <summary>
        /// Run a long, blocking operation behind a silent, in-window modal status overlay
        /// (alert-system-spec.md § Status overlay): a full-shell dimmed scrim + centered card with an
        /// indeterminate spinner, the <paramref name="title"/>, and an optional <paramref name="message"/>
        /// line. Shows the overlay, awaits <paramref name="work"/> (which may hop to a background
        /// thread), and guarantees the overlay is hidden in <c>finally</c> — on success or exception.
        /// The exception is rethrown so the caller can surface a banner if the work fails.
        /// <para>
        /// <paramref name="work"/> receives an <see cref="IProgress{T}"/> of <see cref="StatusUpdate"/>
        /// for mid-operation message updates; the progress object is created on the UI thread so its
        /// reports marshal back automatically. Overlapping scopes are reference-counted: the overlay
        /// hides only when the LAST concurrent scope completes, and the displayed title/message is
        /// last-write-wins. The overlay is non-cancelable (no Esc, no buttons). Must be called on the
        /// UI thread.
        /// </para>
        /// </summary>
        Task RunWithStatusAsync(string title, Func<IProgress<StatusUpdate>, Task> work, string? message = null);
    }

    /// <summary>
    /// The visual banner host the <see cref="AlertService"/> drives. Implemented by the WPF
    /// banner control in the shell; kept as an interface so the service layer does not depend on
    /// the view type. <see cref="Show"/> is always invoked on the UI thread.
    /// </summary>
    public interface IAlertSink
    {
        void Show(AlertItem item);
    }

    /// <summary>
    /// The visual confirm host the <see cref="AlertService"/> drives for bucket-B blocking
    /// decisions. Implemented by the WPF dimmed-scrim overlay in the shell; kept as an interface so
    /// the service layer does not depend on the view type. <see cref="ConfirmAsync"/> is always
    /// invoked on the UI thread and must throw <see cref="System.InvalidOperationException"/> if a
    /// confirm is already showing (one blocking decision at a time).
    /// </summary>
    public interface IConfirmHost
    {
        Task<bool> ConfirmAsync(string message, string title, string confirmLabel, string cancelLabel,
                                bool defaultToConfirm = true);

        Task<ConfirmResult> ConfirmAsync(string message, string title,
                                         string confirmLabel, string declineLabel, string cancelLabel);
    }

    /// <summary>
    /// The visual read-panel host the <see cref="AlertService"/> drives for bucket-C long-text
    /// viewing (alert-system-spec.md Ruling 3). Implemented by the WPF dimmed-scrim overlay in the
    /// shell; kept as an interface so the service layer does not depend on the view type.
    /// <see cref="Show"/> is always invoked on the UI thread.
    /// </summary>
    public interface IReadPanelHost
    {
        void Show(string title, string content);
    }

    /// <summary>
    /// The visual status host the <see cref="AlertService"/> drives for the please-wait overlay
    /// (alert-system-spec.md § Status overlay). Implemented by the WPF dimmed-scrim overlay in the
    /// shell; kept as an interface so the service layer does not depend on the view type. All members
    /// are always invoked on the UI thread (the service marshals).
    /// <para>
    /// These imperative members are internal plumbing — they are deliberately NOT on the public
    /// <see cref="IAlertService"/> surface, which exposes only the scoped
    /// <see cref="IAlertService.RunWithStatusAsync"/> primitive that owns show/update/hide pairing.
    /// </para>
    /// </summary>
    public interface IStatusHost
    {
        /// <summary>True while the overlay is on screen.</summary>
        bool IsShowing { get; }

        /// <summary>Show the overlay with a title and an optional message line.</summary>
        void ShowStatus(string title, string? message);

        /// <summary>Replace the message line mid-operation (reveals it if it was collapsed).</summary>
        void UpdateStatus(string message);

        /// <summary>Collapse the whole overlay.</summary>
        void HideStatus();
    }
}
