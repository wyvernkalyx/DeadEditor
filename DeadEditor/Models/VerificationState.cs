namespace DeadEditor.Models
{
    /// <summary>
    /// Aggregate verification state for a Library row. For single-folder rows, only
    /// <see cref="Unverified"/> and <see cref="Verified"/> are used. For merged
    /// multi-folder rows, <see cref="Partial"/> indicates that some but not all
    /// underlying folder manifests are verified.
    /// </summary>
    public enum VerificationState
    {
        Unverified,
        Partial,
        Verified
    }
}
