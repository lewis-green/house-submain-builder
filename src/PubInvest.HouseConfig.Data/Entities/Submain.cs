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

    /// False when the submain is isolated upstream, so the panel fits none.
    public bool HasIsolator { get; set; } = true;

    /// True when the enclosure is glanded from below, putting the terminations
    /// on the bottom rail instead of the top.
    public bool TerminalsAtBottom { get; set; }

    /// Bumped on every layout change; used for optimistic concurrency.
    public int LayoutVersion { get; set; }

    public List<CircuitRow> Circuits { get; set; } = [];
    public List<DeviceInstance> Devices { get; set; } = [];
}
