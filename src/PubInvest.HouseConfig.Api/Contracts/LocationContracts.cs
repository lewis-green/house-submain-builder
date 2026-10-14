namespace PubInvest.HouseConfig.Api.Contracts;

// The same body adds and renames: a rename replaces the notes too, as the rack
// and panel edits replace theirs.
public sealed record SaveLocationRequest(string Name, string? Notes = null);

public sealed record LocationResponse(Guid Id, string Name, string? Notes, int Sequence);
