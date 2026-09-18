namespace PubInvest.HouseConfig.Domain.Bom;

public sealed record BomLine(
    Guid CatalogueId,
    string PartNumber,
    string Description,
    int Quantity,
    /// False for anything bought but never mounted on the rail — the LED driver
    /// above all — so nobody goes looking for it in the panel.
    bool PanelMounted = true);

/// What to order. Deliberately carries no prices: this is a parts list, and
/// costing happens wherever your pricing actually lives.
public sealed record BillOfMaterials(IReadOnlyList<BomLine> Lines);
