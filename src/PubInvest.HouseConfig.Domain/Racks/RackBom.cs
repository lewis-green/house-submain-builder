using PubInvest.HouseConfig.Domain.Bom;

namespace PubInvest.HouseConfig.Domain.Racks;

/// What to order for a rack: the cabinet, each item, and whatever an item
/// cannot be mounted without. A parts list, so no prices — as the panel's.
public static class RackBom
{
    /// `placed` has one entry per item in the rack. The catalogue must hold every
    /// accessory those items require; the revision service checks that first.
    public static BillOfMaterials Build(
        RackType cabinet,
        IEnumerable<RackDeviceType> placed,
        IReadOnlyDictionary<Guid, RackDeviceType> catalogue)
    {
        var items = placed.ToList();
        var lines = new List<BomLine> { new(cabinet.Id, cabinet.PartNumber, cabinet.Description, 1) };

        lines.AddRange(items
            .GroupBy(t => t.Id)
            .Select(g => new BomLine(g.Key, g.First().PartNumber, g.First().Description, g.Count())));

        lines.AddRange(items
            .Where(t => t.RequiredAccessoryId is not null)
            .GroupBy(t => t.RequiredAccessoryId!.Value)
            .Select(g =>
            {
                var accessory = catalogue[g.Key];
                return new BomLine(accessory.Id, accessory.PartNumber, accessory.Description, g.Count());
            }));

        return new BillOfMaterials(lines.OrderBy(l => l.PartNumber, StringComparer.Ordinal).ToList());
    }
}
