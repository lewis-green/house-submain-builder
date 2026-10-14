namespace PubInvest.HouseConfig.Data.Entities;

/// An issued rack: everything needed to redraw it, schedule its ports and list
/// its parts, embedded rather than referenced, as panel revisions are.
public class RackRevision
{
    public Guid Id { get; set; }
    public Guid RackId { get; set; }
    public int LayoutVersion { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = "";
}
