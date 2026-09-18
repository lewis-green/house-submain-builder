namespace PubInvest.HouseConfig.Domain.Bom;

public sealed record BomLine(
    Guid CatalogueId,
    string PartNumber,
    string Description,
    int Quantity,
    decimal UnitCost,
    /// False for anything that is bought but never mounted on the rail — the LED
    /// driver above all — so nobody goes looking for it in the panel.
    bool PanelMounted = true)
{
    public decimal LineTotal => Quantity * UnitCost;
}

public sealed record BillOfMaterials(IReadOnlyList<BomLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}
