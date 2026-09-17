namespace PubInvest.HouseConfig.Data.Entities;

public class DeviceInstance
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public Guid DeviceTypeId { get; set; }
    public string Category { get; set; } = "";
    public int RowIndex { get; set; }
    public int StartSlot { get; set; }
    public int ModuleWidth { get; set; }
    public string Label { get; set; } = "";
    public string TerminalRole { get; set; } = "None";
    public List<DeviceChannelRow> Channels { get; set; } = [];
}
