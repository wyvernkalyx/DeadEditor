using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// A real-world event referenced by a <see cref="BoxSetDefinition"/>: date, venue,
    /// and setlist. Disc tracks reference a concert by <see cref="Id"/>. The concert
    /// axis is orthogonal to the disc axis — a single concert can span disc boundaries.
    /// </summary>
    public class BoxSetConcert
    {
        public string Id { get; set; } = "";                // typically the date, e.g. "1971-12-09"
        public string Date { get; set; } = "";              // yyyy-MM-dd
        public string Venue { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        public List<BoxSetSet> Setlist { get; set; } = new();
    }
}
