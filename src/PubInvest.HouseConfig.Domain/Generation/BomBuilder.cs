using PubInvest.HouseConfig.Domain.Bom;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

public static class BomBuilder
{
    public static BillOfMaterials Build(
        PanelLayout layout,
        IReadOnlyList<AccessoryLine> accessories,
        EnclosureType enclosure,
        DeviceCatalogue catalogue)
    {
        var quantities = new Dictionary<Guid, int>();

        foreach (var device in layout.Devices)
        {
            quantities[device.DeviceTypeId] = quantities.GetValueOrDefault(device.DeviceTypeId) + 1;
        }

        foreach (var accessory in accessories)
        {
            quantities[accessory.DeviceTypeId] =
                quantities.GetValueOrDefault(accessory.DeviceTypeId) + accessory.Quantity;
        }

        var lines = new List<BomLine>
        {
            new(enclosure.Id, enclosure.Description, enclosure.Description, 1, enclosure.Cost)
        };

        foreach (var (deviceTypeId, quantity) in quantities)
        {
            var deviceType = catalogue.Find(deviceTypeId);

            // A device type missing from the catalogue is skipped: the stage that chose
            // it has already raised NO_PREFERRED_DEVICE, and repeating it here would
            // only clutter the UI.
            if (deviceType is null) continue;

            lines.Add(new BomLine(
                deviceType.Id,
                deviceType.PartNumber,
                deviceType.Description,
                quantity,
                deviceType.Cost));
        }

        return new BillOfMaterials(
            lines.OrderBy(l => l.PartNumber, StringComparer.Ordinal).ToList());
    }
}
