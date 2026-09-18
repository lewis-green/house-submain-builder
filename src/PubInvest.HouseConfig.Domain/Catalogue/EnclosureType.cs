namespace PubInvest.HouseConfig.Domain.Catalogue;

public sealed record EnclosureType(
    Guid Id,
    string Manufacturer,
    string Model,
    int Rows,
    int SlotsPerRow,
    string IpRating)
{
    public int TotalSlots => Rows * SlotsPerRow;

    public string Description => $"{Manufacturer} {Model}";
}
