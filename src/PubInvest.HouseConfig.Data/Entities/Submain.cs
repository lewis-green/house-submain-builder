namespace PubInvest.HouseConfig.Data.Entities;

public class Submain
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
    public string? Reference { get; set; }
    public string? FeedCableSize { get; set; }
    public int? OriginBreakerAmps { get; set; }
    public string? Phase { get; set; }
    public Guid? EnclosureTypeId { get; set; }
    public Guid? RuleSetId { get; set; }
    public string? Notes { get; set; }

    /// Bumped on every layout change; used for optimistic concurrency.
    public int LayoutVersion { get; set; }

    public List<CircuitRow> Circuits { get; set; } = [];
    public List<DeviceInstance> Devices { get; set; } = [];
}
