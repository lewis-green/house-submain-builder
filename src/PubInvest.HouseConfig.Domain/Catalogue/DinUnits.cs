namespace PubInvest.HouseConfig.Domain.Catalogue;

/// Every width in this domain — `DeviceType.ModuleWidth`, `EnclosureType.SlotsPerRow`,
/// `PlacedDevice.StartSlot` — is counted in *slot units*, not in DIN modules.
///
/// One slot unit is a third of a DIN module (T), because three WAGO 2003-7646
/// terminal blocks fit in the space of one T. Using the third as the base unit
/// keeps every width a whole number, which is what lets the packer stay integer
/// arithmetic with no rounding.
///
///   Shelly Pro Dimmer  1 T   = 3 slot units
///   Shelly Pro relay   3 T   = 9 slot units
///   WAGO 2003-7646     1/3 T = 1 slot unit
///   A 12-way row       12 T  = 36 slot units
///
/// Anything showing widths to a person — the panel drawing, the PDF, an
/// enclosure picker — divides by <see cref="PerModule"/> to get DIN modules.
public static class DinUnits
{
    /// Slot units in one DIN module (T).
    public const int PerModule = 3;

    public static decimal ToModules(int slotUnits) => slotUnits / (decimal)PerModule;

    public static int FromModules(int modules) => modules * PerModule;
}
