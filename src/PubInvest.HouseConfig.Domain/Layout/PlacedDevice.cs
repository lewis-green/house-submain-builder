using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Layout;

public enum TerminalRole
{
    None,

    /// A 3-tier block carrying all three conductors for one circuit.
    All,

    /// Single-conductor banks. Kept only so revisions issued before the 3-tier
    /// block was adopted still deserialise.
    Line,
    Neutral,
    Earth,
}

public sealed record ChannelAssignment(int ChannelIndex, Guid? CircuitId, bool IsSpare);

public sealed record PlacedDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole)
{
    public int EndSlotExclusive => StartSlot + ModuleWidth;
}
