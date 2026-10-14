using Microsoft.EntityFrameworkCore;
using Npgsql;
using PubInvest.HouseConfig.Data;

namespace PubInvest.HouseConfig.Api.Locations;

/// What a panel or rack may say about where it is. Shared by the submain and
/// rack endpoints, so neither depends on the other's code or on the location
/// endpoints.
public static class LocationRules
{
    public const string NotInThisHouse = "Choose a location in this house, or none.";

    /// The problem to report when a panel or rack names a location outside its
    /// own house — or one that does not exist — or null when the location is
    /// fine. No location at all is always fine: it means "not placed".
    public static async Task<string?> ProblemAsync(
        HouseConfigDbContext db, Guid projectId, Guid? locationId, CancellationToken ct)
        => locationId is { } id && !await db.Locations.AnyAsync(l => l.Id == id && l.ProjectId == projectId, ct)
            ? NotInThisHouse
            : null;

    /// True when a save failed because the location it named has gone — removed
    /// by someone else after ProblemAsync passed it. Told apart from every other
    /// missing reference by the foreign key's name, which ends in the column.
    public static bool IsMissingLocation(DbUpdateException e)
        => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } fk
           && fk.ConstraintName?.EndsWith("_Locations_LocationId", StringComparison.Ordinal) == true;

    /// The same answer ProblemAsync's check gives, for a save that lost the race.
    public static IResult Misplaced()
        => Results.ValidationProblem(new Dictionary<string, string[]> { ["locationId"] = [NotInThisHouse] });
}
