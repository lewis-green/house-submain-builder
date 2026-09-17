using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Layout;

public enum TerminalRole { None, Line, Neutral, Earth }

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
