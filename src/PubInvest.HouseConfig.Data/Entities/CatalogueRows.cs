namespace PubInvest.HouseConfig.Data.Entities;

public class DeviceTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Category { get; set; } = "";
    public int ModuleWidth { get; set; }
    public int ChannelCount { get; set; }
    public int? MaxLoadPerChannelW { get; set; }
    public int? MaxTotalLoadW { get; set; }
    public decimal Cost { get; set; }
    public bool Active { get; set; }
}

public class EnclosureTypeRow
{
    public Guid Id { get; set; }
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public int Rows { get; set; }
    public int SlotsPerRow { get; set; }
    public string IpRating { get; set; } = "";
    public decimal Cost { get; set; }
}

public class RuleSetRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string PayloadJson { get; set; } = "";
    public bool IsDefault { get; set; }
}
