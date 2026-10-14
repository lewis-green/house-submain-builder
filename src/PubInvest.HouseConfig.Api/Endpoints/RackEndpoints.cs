using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Locations;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackEndpoints
{
    public static IEndpointRouteBuilder MapRackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{id:guid}/racks", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var racks = await db.Racks.Include(r => r.Items)
                    .Where(r => r.ProjectId == id).OrderBy(r => r.Name).ToListAsync(ct);

                var responses = new List<RackResponse>();
                foreach (var rack in racks) responses.Add(await RackViews.ResponseAsync(db, rack, ct));
                return Results.Ok(responses);
            })
            .WithTags("Racks");

        app.MapPost("/projects/{id:guid}/racks", async (
                Guid id,
                SaveRackRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var problems = await RackViews.ValidateAsync(db, id, request, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                var rack = new Rack
                {
                    Id = Guid.NewGuid(),
                    ProjectId = id,
                    Name = request.Name.Trim(),
                    RackTypeId = request.RackTypeId,
                    FedFromSubmainId = request.FedFromSubmainId,
                    Notes = request.Notes,
                    LocationId = request.LocationId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                db.Racks.Add(rack);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException e) when (LocationRules.IsMissingLocation(e))
                {
                    return LocationRules.Misplaced();
                }

                return Results.Created($"/racks/{rack.Id}", await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapGet("/racks/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                return rack is null ? Results.NotFound() : Results.Ok(await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapPatch("/racks/{id:guid}", async (
                Guid id,
                SaveRackRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                if (rack is null) return Results.NotFound();

                var problems = await RackViews.ValidateAsync(db, rack.ProjectId, request, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // A shorter cabinet is refused rather than items being shuffled:
                // the person changing it is told exactly what to move first.
                if (request.RackTypeId != rack.RackTypeId)
                {
                    var cabinet = await db.RackTypes.SingleAsync(t => t.Id == request.RackTypeId, ct);
                    var spans = await RackViews.SpansAsync(db, rack, ct);
                    if (RackPlacement.Blocking(cabinet.HeightU, spans) is { } blocked)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["rackTypeId"] =
                            [
                                $"A {cabinet.HeightU}U cabinet is too short for what is in this rack. " +
                                $"Move or remove what's in U{blocked.From}–U{blocked.To} first."
                            ]
                        });
                    }
                }

                rack.Name = request.Name.Trim();
                rack.RackTypeId = request.RackTypeId;
                rack.FedFromSubmainId = request.FedFromSubmainId;
                rack.Notes = request.Notes;
                rack.LocationId = request.LocationId;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // An item was added, moved or removed between our read and our
                    // save, so the cabinet check above may be stale.
                    db.ChangeTracker.Clear();
                    var current = await db.Racks.Where(r => r.Id == id).Select(r => r.LayoutVersion).SingleAsync(ct);
                    return Results.Json(
                        new ConflictResponse("This rack changed since you loaded it. Reload and try again.", current),
                        statusCode: StatusCodes.Status409Conflict);
                }
                catch (DbUpdateException e) when (LocationRules.IsMissingLocation(e))
                {
                    // The location was removed after the check above passed it.
                    return LocationRules.Misplaced();
                }

                return Results.Ok(await RackViews.ResponseAsync(db, rack, ct));
            })
            .WithTags("Racks");

        app.MapGet("/racks/{id:guid}/layout", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
                await RackViews.LayoutAsync(db, id, ct) is { } layout ? Results.Ok(layout) : Results.NotFound())
            .WithTags("Racks");

        // Its own endpoint rather than a field on the submain: the submain
        // response is built in many places, and only the panel screen asks.
        app.MapGet("/submains/{id:guid}/racks", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
                await db.Racks
                    .Where(r => r.FedFromSubmainId == id)
                    .OrderBy(r => r.Name)
                    .Select(r => new FedRackResponse(r.Id, r.Name))
                    .ToListAsync(ct))
            .WithTags("Racks");

        return app;
    }
}
