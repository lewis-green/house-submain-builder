namespace PubInvest.HouseConfig.Data.Entities;

public class CircuitRow
{
    public Guid Id { get; set; }
    public Guid SubmainId { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Room { get; set; }
    public int Sequence { get; set; }
    public decimal? WattsPerMetre { get; set; }
    public decimal? LengthMetres { get; set; }
}
