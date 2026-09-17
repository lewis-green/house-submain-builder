namespace PubInvest.HouseConfig.Data.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<Submain> Submains { get; set; } = [];
}
