namespace PubInvest.HouseConfig.Data.Entities;

public class PanelRevision
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public int LayoutVersion { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = "";
}
