namespace PubInvest.HouseConfig.Data.Entities;

public class DeviceChannelRow
{
    public Guid Id { get; set; }
    public Guid DeviceInstanceId { get; set; }
    public int ChannelIndex { get; set; }
    public Guid? CircuitId { get; set; }
    public bool IsSpare { get; set; }
}
