using PubInvest.HouseConfig.Domain.Catalogue;

namespace PubInvest.HouseConfig.Domain.Layout;

/// Drawing geometry in millimetres, shared by anything that draws a panel.
///
/// This is a drawing scale, not life size: a real DIN module is 17.5mm, here it
/// draws at 15mm so a 24-module row fits an A4 landscape page with margin.
public static class PanelGeometry
{
    public const double SlotMm = 5.0;
    public const double RowMm = 45.0;
    public const double RowGapMm = 10.0;

    public static double SlotToX(int slot) => slot * SlotMm;

    public static double RowToY(int row) => row * (RowMm + RowGapMm);

    public static (double X, double Y, double Width, double Height) DeviceBox(PlacedDevice device) =>
        (SlotToX(device.StartSlot), RowToY(device.RowIndex), SlotToX(device.ModuleWidth), RowMm);

    public static (double Width, double Height) PanelSize(PanelLayout layout) =>
        (SlotToX(layout.SlotsPerRow),
         layout.Rows == 0 ? 0 : layout.Rows * RowMm + (layout.Rows - 1) * RowGapMm);

    /// One DIN module in drawing millimetres.
    public static double ModuleMm => SlotMm * DinUnits.PerModule;
}
