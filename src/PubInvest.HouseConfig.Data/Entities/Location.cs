namespace PubInvest.HouseConfig.Data.Entities;

/// A place in a house where equipment lives — a plant room, a garage, a comms
/// cupboard. Automation panels and racks point at one; neither belongs to it,
/// so removing a location leaves what was in it unplaced rather than gone.
public class Location
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// The order the house page lists locations in: the order they were added.
    public int Sequence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
