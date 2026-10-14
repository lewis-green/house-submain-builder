namespace PubInvest.HouseConfig.Data.Entities;

public class RackTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public int HeightU { get; set; }

    /// "Wall" or "Floor", for display: it changes nothing about placement.
    public string Mounting { get; set; } = "Wall";
}

public class RackDeviceTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Kind { get; set; } = "";
    public int HeightU { get; set; }

    /// The item's port groups as JSON. They are only ever read and written
    /// whole, so a table of their own would buy nothing but joins.
    public string PortGroupsJson { get; set; } = "[]";

    public int? PoeBudgetW { get; set; }
    public Guid? RequiredAccessoryId { get; set; }
    public bool Active { get; set; }
}
