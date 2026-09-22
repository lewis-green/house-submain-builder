namespace PubInvest.HouseConfig.Data.Entities;

/// A device on this submain's panel that no circuit asks for: an energy meter,
/// a LAN switch, a relay held in reserve.
public class ExtraFixtureRow
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public Guid DeviceTypeId { get; set; }
    public int Quantity { get; set; }
    public int Sequence { get; set; }
}
