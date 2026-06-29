using DeadEditor.Models;
using DeadEditor.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeadEditor
{
    /// <summary>
    /// One row of the Match Setlist review surface, wrapping a single
    /// <see cref="SetlistMatcher.TrackProposal"/>. Carries two independently
    /// resolvable decisions — one for SongName, one for Segue (spec §4) — each
    /// pre-seeded from <see cref="ReviewDefaultPolicy"/>.
    ///
    /// The "edited-New" resolution lives in <see cref="EffectiveSongName"/> /
    /// <see cref="EffectiveSegue"/>: an Accepted field takes the new (or
    /// hand-tuned) value; an Ignored field falls back to the captured Old
    /// value. The container rebuilds a fresh proposal from these effective
    /// values; the wrapped <see cref="Source"/> proposal is never mutated.
    /// </summary>
    public class ReviewRowViewModel : INotifyPropertyChanged
    {
        private readonly SetlistMatcher.TrackProposal _source;

        public ReviewRowViewModel(SetlistMatcher.TrackProposal proposal)
        {
            _source = proposal;
            OldSongName = proposal.OldSongName;
            NewSongName = proposal.NewSongName;
            _editableSongName = proposal.NewSongName; // seeded with the proposed canonical (spec §4)
            OldSegue = proposal.OldSegue;
            NewSegue = proposal.NewSegue;
            _songNameDecision = ReviewDefaultPolicy.DefaultForSongName(OldSongName, NewSongName);
            _segueDecision = ReviewDefaultPolicy.DefaultForSegue(OldSegue, NewSegue);
        }

        /// <summary>The wrapped proposal — the container reads its Track and
        /// CoveredEntryIndices when rebuilding the edited set. Never mutated.</summary>
        public SetlistMatcher.TrackProposal Source => _source;

        /// <summary>The audio track this row decorates (for display + write-back).</summary>
        public TrackInfo Track => _source.Track;

        // --- Current-identity pass-throughs (mirror the album-detail grid) ---
        // These read the SAME TrackInfo fields the EditMetadataView grid binds
        // (# = TrackNumber, Disc = DiscNumber, Title = SongName, Time = Duration)
        // so the dialog header can never diverge from the grid behind it. They
        // deliberately do NOT use the disc-concatenated DisplayTrackNumber or any
        // setlist-derived/renumbered value. Read live, so a Renumber before
        // Match Setlist is reflected here too.

        /// <summary>The track's current number as the grid shows it: plain
        /// TrackNumber and DiscNumber (the grid's "#" and "Disc" columns),
        /// never the disc-concatenated DisplayTrackNumber.</summary>
        public string TrackNumberDisplay => $"#{Track.TrackNumber} · Disc {Track.DiscNumber}";

        /// <summary>The track's current song title (the grid's Title column in
        /// edit mode = SongName, falling back to Title).</summary>
        public string TrackTitleDisplay =>
            string.IsNullOrEmpty(Track.SongName) ? Track.Title : Track.SongName;

        /// <summary>The track's duration as the grid's Time column shows it.</summary>
        public string TrackDurationDisplay => Track.Duration;

        public string OldSongName { get; }
        public string NewSongName { get; }
        public bool OldSegue { get; }
        public bool NewSegue { get; }

        private string _editableSongName;
        /// <summary>Always-visible editable SongName (spec §4, decision (b)),
        /// seeded with the proposed canonical so the user can take it, ignore
        /// it, or hand-tune it.</summary>
        public string EditableSongName
        {
            get => _editableSongName;
            set
            {
                if (_editableSongName != value)
                {
                    _editableSongName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectiveSongName));
                }
            }
        }

        private ReviewDecision _songNameDecision;
        public ReviewDecision SongNameDecision
        {
            get => _songNameDecision;
            set
            {
                if (_songNameDecision != value)
                {
                    _songNameDecision = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSongNameAccepted));
                    OnPropertyChanged(nameof(EffectiveSongName));
                }
            }
        }

        private ReviewDecision _segueDecision;
        public ReviewDecision SegueDecision
        {
            get => _segueDecision;
            set
            {
                if (_segueDecision != value)
                {
                    _segueDecision = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSegueAccepted));
                    OnPropertyChanged(nameof(EffectiveSegue));
                }
            }
        }

        /// <summary>Convenience bool view of <see cref="SongNameDecision"/> for
        /// a simple Accept checkbox binding (Accept = checked, Ignore = unchecked).</summary>
        public bool IsSongNameAccepted
        {
            get => SongNameDecision == ReviewDecision.Accept;
            set => SongNameDecision = value ? ReviewDecision.Accept : ReviewDecision.Ignore;
        }

        /// <summary>Convenience bool view of <see cref="SegueDecision"/>.</summary>
        public bool IsSegueAccepted
        {
            get => SegueDecision == ReviewDecision.Accept;
            set => SegueDecision = value ? ReviewDecision.Accept : ReviewDecision.Ignore;
        }

        /// <summary>True when the proposed SongName actually differs from the
        /// current one (Ordinal). Drives showing the name sub-block in the
        /// dialog so a segue-only row carries no redundant name control.</summary>
        public bool SongNameChanged =>
            !string.Equals(OldSongName, NewSongName, System.StringComparison.Ordinal);

        /// <summary>True when the proposed Segue differs from the current one.
        /// Drives showing the segue sub-block in the dialog.</summary>
        public bool SegueChanged => OldSegue != NewSegue;

        /// <summary>A row is a no-op only by conjunction (spec §4): the name is
        /// unchanged AND the segue is unchanged. A name-equal row whose segue
        /// would change is NOT a no-op and stays visible. (Equivalently: a row
        /// is visible when <see cref="SongNameChanged"/> or
        /// <see cref="SegueChanged"/>.)</summary>
        public bool IsNoOp => !SongNameChanged && !SegueChanged;

        /// <summary>True when this row collapses 2+ official setlist entries into
        /// one media file. A combine is always shown for confirmation, exempt from
        /// no-op hiding (spec §6.1): the human confirms the <i>combine</i>
        /// (coverage of multiple official entries), not a text diff, so even a
        /// name-no-op combine must surface.</summary>
        public bool IsCombined => Source.CoveredEntryIndices.Count > 1;

        /// <summary>The resolved SongName: the editable value when Accepted,
        /// the captured Old value when Ignored.</summary>
        public string EffectiveSongName =>
            SongNameDecision == ReviewDecision.Accept ? EditableSongName : OldSongName;

        /// <summary>The resolved Segue: the proposed New flag when Accepted, the
        /// captured Old flag when Ignored (this is what protects a real segue
        /// from a blind clear).</summary>
        public bool EffectiveSegue =>
            SegueDecision == ReviewDecision.Accept ? NewSegue : OldSegue;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
