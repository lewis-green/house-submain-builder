namespace PubInvest.HouseConfig.Data.Entities;

/// A network rack in a house. It sits beside the submains rather than inside
/// one: the submain that feeds it is a fact about the rack, recorded as a link
/// that can be blank or change without the rack moving.
public class Rack
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public Guid RackTypeId { get; set; }
    public Guid? FedFromSubmainId { get; set; }
    public string? Notes { get; set; }

    /// Bumped by every add, move and removal. A concurrency token, so two adds
    /// racing for the same U cannot both land.
    public int LayoutVersion { get; set; }

    /// How many switches and patch panels this rack has ever named. A removed
    /// item's name is never handed to a later one, so "Switch 2" on a printed
    /// drawing keeps meaning the same switch.
    public int SwitchesNamed { get; set; }
    public int PatchPanelsNamed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public List<RackItem> Items { get; set; } = [];
}

public class RackItem
{
    public Guid Id { get; set; }
    public Guid RackId { get; set; }
    public Guid RackDeviceTypeId { get; set; }

    /// The highest U the item occupies; U1 is the bottom of the cabinet.
    public int TopU { get; set; }

    public int Sequence { get; set; }
    public string Label { get; set; } = "";
    public List<RackPort> Ports { get; set; } = [];
}

/// Stored only once a port has a label or a PoE tick, so a new 48-port switch
/// writes nothing here.
public class RackPort
{
    public Guid RackItemId { get; set; }
    public int Number { get; set; }
    public string? Label { get; set; }
    public bool Poe { get; set; }
}
