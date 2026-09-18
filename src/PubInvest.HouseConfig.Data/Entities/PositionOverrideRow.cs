namespace PubInvest.HouseConfig.Data.Entities;

/// Survives the device rows being replaced on re-generation, which is the whole
/// point: it is keyed by label, not by device id.
public class PositionOverrideRow
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public string Label { get; set; } = "";
    public int RowIndex { get; set; }
    public int StartSlot { get; set; }
}
