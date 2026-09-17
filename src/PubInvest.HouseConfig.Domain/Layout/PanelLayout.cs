namespace PubInvest.HouseConfig.Domain.Layout;

public sealed record PanelLayout(int Rows, int SlotsPerRow, IReadOnlyList<PlacedDevice> Devices)
{
    public IEnumerable<PlacedDevice> DevicesInRow(int rowIndex)
        => Devices.Where(d => d.RowIndex == rowIndex).OrderBy(d => d.StartSlot);

    public int SlotsUsed => Devices.Sum(d => d.ModuleWidth);

    public int TotalSlots => Rows * SlotsPerRow;
}
