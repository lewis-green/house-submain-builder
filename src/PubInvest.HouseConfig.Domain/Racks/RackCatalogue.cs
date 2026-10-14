namespace PubInvest.HouseConfig.Domain.Racks;

public enum RackDeviceKind
{
    Switch,
    PatchPanel,

    /// Bought with an item but never placed in the rack, such as the rack-mount
    /// kit a half-width switch needs. It reaches the bill of materials only
    /// through the item that names it.
    Accessory,
}

/// A run of consecutively numbered ports that share a name ("1G", "SFP+") and
/// whether the PoE tick is offered on them.
public sealed record PortGroup(string Name, int From, int To, bool PoeTick)
{
    public int Count => To - From + 1;

    public bool Contains(int number) => number >= From && number <= To;
}

/// A rack cabinet. Heights are in rack units; U1 is the bottom of the cabinet.
public sealed record RackType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    int HeightU,
    string Mounting)
{
    public string Description => $"{Manufacturer} {Model}";
}

/// Something that can go in a rack, or be bought with something that does.
public sealed record RackDeviceType(
    Guid Id,
    string Manufacturer,
    string Model,
    string PartNumber,
    RackDeviceKind Kind,
    int HeightU,
    IReadOnlyList<PortGroup> PortGroups,
    int? PoeBudgetW,
    Guid? RequiredAccessoryId,
    bool Active)
{
    public string Description => $"{Manufacturer} {Model}";

    public int PortCount => PortGroups.Sum(g => g.Count);

    public PortGroup? GroupOf(int number) => PortGroups.FirstOrDefault(g => g.Contains(number));
}
