using Microsoft.EntityFrameworkCore;
using Npgsql;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{id:guid}/locations", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                return Results.Ok(await db.Locations
                    .Where(l => l.ProjectId == id)
                    .OrderBy(l => l.Sequence).ThenBy(l => l.CreatedAt)
                    .Select(l => new LocationResponse(l.Id, l.Name, l.Notes, l.Sequence))
                    .ToListAsync(ct));
            })
            .WithTags("Locations");

        app.MapPost("/projects/{id:guid}/locations", async (
                Guid id,
                SaveLocationRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var problems = await ValidateAsync(db, id, null, request.Name, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                var location = new Location
                {
                    Id = Guid.NewGuid(),
                    ProjectId = id,
                    Name = request.Name.Trim(),
                    Notes = request.Notes,
                    Sequence = (await db.Locations.Where(l => l.ProjectId == id).MaxAsync(l => (int?)l.Sequence, ct) ?? -1) + 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                db.Locations.Add(location);
                if (await SaveAsync(db, id, location.Name, ct) is { } refusal) return refusal;

                return Results.Created($"/locations/{location.Id}", ToResponse(location));
            })
            .WithTags("Locations");

        app.MapPatch("/locations/{id:guid}", async (
                Guid id,
                SaveLocationRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id, ct);
                if (location is null) return Results.NotFound();

                var problems = await ValidateAsync(db, location.ProjectId, id, request.Name, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                location.Name = request.Name.Trim();
                location.Notes = request.Notes;
                if (await SaveAsync(db, location.ProjectId, location.Name, ct) is { } refusal) return refusal;

                return Results.Ok(ToResponse(location));
            })
            .WithTags("Locations");

        // The database unplaces the panels and racks that were here (ON DELETE
        // SET NULL); they stay in the house, listed under "Not placed".
        app.MapDelete("/locations/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
                await db.Locations.Where(l => l.Id == id).ExecuteDeleteAsync(ct) == 0
                    ? Results.NotFound()
                    : Results.NoContent())
            .WithTags("Locations");

        return app;
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        HouseConfigDbContext db, Guid projectId, Guid? self, string? name, CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            problems["name"] = ["A location name is required."];
            return problems;
        }

        // ToLower on both sides rather than leaning on the column's collation, so
        // the check reads as what it does; the unique index backs it up.
        var lowered = name.Trim().ToLower();
        var clash = await db.Locations
            .Where(l => l.ProjectId == projectId && l.Id != self && l.Name.ToLower() == lowered)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct);

        if (clash is not null) problems["name"] = [Duplicate(clash)];
        return problems;
    }

    /// Saves, turning the unique index's refusal — two people adding the same
    /// name at once, past the check — into the same answer the check gives.
    private static async Task<IResult?> SaveAsync(HouseConfigDbContext db, Guid projectId, string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [Duplicate(name)] });
        }
    }

    private static string Duplicate(string name) => $"There is already a location called {name} in this house.";

    private static LocationResponse ToResponse(Location l) => new(l.Id, l.Name, l.Notes, l.Sequence);
}
