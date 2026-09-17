using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

/// A device the generator has decided is needed, before it has been given a slot.
public sealed record RequiredDevice(
    Guid DeviceTypeId,
    DeviceCategory Category,
    int ModuleWidth,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels,
    TerminalRole TerminalRole = TerminalRole.None);

/// A part counted in the BOM that occupies no DIN slots (jumper bars, end stops).
public sealed record AccessoryLine(Guid DeviceTypeId, int Quantity);
