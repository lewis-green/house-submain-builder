namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record CreateProjectRequest(string Name, string? Address, string? Notes);

public sealed record ProjectResponse(Guid Id, string Name, string? Address, string? Notes, int SubmainCount);
