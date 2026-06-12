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
        /// Two-way only (Yes/No or OK/Cancel) — <c>true</c> = the affirmative button
        /// (<paramref name="confirmLabel"/>) was chosen; <c>false</c> = the negative/safe button
        /// (<paramref name="cancelLabel"/>) OR Esc. The three-way (Yes/No/Cancel) overload and the
        /// <c>ConfirmResult</c> enum are deferred to the increment that converts site #27 (the sole
        /// YesNoCancel site, bucket-B sweep) — they are not trivially shared with this two-way path
        /// (a third button + tri-state result), and the spec forbids stubbed NotImplemented members.
        /// </para>
        /// <remarks>
        /// Re-entrancy: calling this while a confirm is already showing throws
        /// <see cref="System.InvalidOperationException"/> — one blocking decision owns the surface at
        /// a time (the scrim structurally prevents a second user-initiated confirm, so re-entrancy is
        /// a logic error, not a queue case). Must be called on the UI thread.
        /// </remarks>
        /// </summary>
        Task<bool> ConfirmAsync(string message, string title,
                                string confirmLabel = "Yes", string cancelLabel = "No");
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
        Task<bool> ConfirmAsync(string message, string title, string confirmLabel, string cancelLabel);
    }
}
