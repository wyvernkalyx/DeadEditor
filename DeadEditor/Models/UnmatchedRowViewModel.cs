using DeadEditor.Models;

namespace DeadEditor
{
    /// <summary>
    /// One row of the Match Setlist review surface's read-only <b>Unmatched</b>
    /// section: a track the compute produced no proposal for (nothing to
    /// accept/reject — the user resolves it manually in the grid). A lightweight
    /// display wrapper over the underlying <see cref="TrackInfo"/>, carrying no
    /// accept state, unlike <see cref="ReviewRowViewModel"/>.
    ///
    /// The two display strings deliberately mirror the matched-row header
    /// (<see cref="ReviewRowViewModel"/>, current-identity pass-throughs) so an
    /// unmatched row reads identically to a matched one: the grid's plain
    /// TrackNumber + DiscNumber (never the disc-concatenated DisplayTrackNumber),
    /// and the current SongName (falling back to Title).
    /// </summary>
    public sealed class UnmatchedRowViewModel
    {
        private readonly TrackInfo _track;

        public UnmatchedRowViewModel(TrackInfo track)
        {
            _track = track;
        }

        /// <summary>The audio track this row surfaces.</summary>
        public TrackInfo Track => _track;

        /// <summary>The track's current number as the grid shows it: plain
        /// TrackNumber and DiscNumber, mirroring
        /// <see cref="ReviewRowViewModel.TrackNumberDisplay"/>.</summary>
        public string TrackNumberDisplay => $"#{_track.TrackNumber} · Disc {_track.DiscNumber}";

        /// <summary>The track's current song title (SongName, falling back to
        /// Title), mirroring <see cref="ReviewRowViewModel.TrackTitleDisplay"/>.</summary>
        public string TrackTitleDisplay =>
            string.IsNullOrEmpty(_track.SongName) ? _track.Title : _track.SongName;
    }
}
