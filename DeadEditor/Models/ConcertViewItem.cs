using System.ComponentModel;

namespace DeadEditor.Models;

/// <summary>
/// Base class for items displayed in the concert view track list.
/// Used to create a flat list containing both date headers and tracks.
/// </summary>
public abstract class ConcertViewItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a date section header in the concert view.
/// Displays date, venue, and location with expand/collapse functionality.
/// </summary>
public class DateHeaderItem : ConcertViewItem
{
    private bool _isExpanded;

    public string Date { get; set; } = "";
    public string Venue { get; set; } = "";
    public string Location { get; set; } = "";
    public int TrackCount { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronIcon));
            }
        }
    }

    /// <summary>
    /// Chevron icon: "▼" when expanded, "▶" when collapsed
    /// </summary>
    public string ChevronIcon => IsExpanded ? "▼ " : "▶ ";

    /// <summary>
    /// Full header text: "yyyy-MM-dd — Venue, City, ST"
    /// </summary>
    public string HeaderText
    {
        get
        {
            if (!string.IsNullOrEmpty(Venue) && !string.IsNullOrEmpty(Location))
            {
                return $"{Date} — {Venue}, {Location}";
            }
            else if (!string.IsNullOrEmpty(Venue))
            {
                return $"{Date} — {Venue}";
            }
            else
            {
                return Date;
            }
        }
    }
}

/// <summary>
/// Represents a track row in the concert view.
/// Wraps a TrackInfo instance for display in the track list.
/// </summary>
public class TrackViewItem : ConcertViewItem
{
    public TrackInfo Track { get; set; } = null!;
    public DateHeaderItem ParentHeader { get; set; } = null!;
}
