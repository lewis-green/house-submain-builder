namespace PubInvest.HouseConfig.Domain.Bom;

public sealed record BomLine(
    Guid CatalogueId,
    string PartNumber,
    string Description,
    int Quantity,
    decimal UnitCost)
{
    public decimal LineTotal => Quantity * UnitCost;
}

public sealed record BillOfMaterials(IReadOnlyList<BomLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}
