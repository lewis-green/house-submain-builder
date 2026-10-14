using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Locations;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Domain.Circuits;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class SubmainEndpoints
{
    public static IEndpointRouteBuilder MapSubmainEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{projectId:guid}/submains",
                async (Guid projectId, HouseConfigDbContext db, CancellationToken ct) =>
                    await db.Submains
                        .Where(s => s.ProjectId == projectId)
                        .OrderBy(s => s.Name)
                        .Select(ToResponse)
                        .ToListAsync(ct))
            .WithTags("Submains");

        app.MapPost("/projects/{projectId:guid}/submains", async (
                Guid projectId,
                CreateSubmainRequest request,
                HouseConfigDbContext db,
                DesignService designs,
                CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return Results.NotFound();

                var problems = Validate(request.Name, request.Circuits);
                if (await LocationRules.ProblemAsync(db, projectId, request.LocationId, ct) is { } misplaced)
                {
                    problems["locationId"] = [misplaced];
                }
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                var submain = new Submain
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Name = request.Name.Trim(),
                    Reference = request.Reference,
                    FeedCableSize = request.FeedCableSize,
                    OriginBreakerAmps = request.OriginBreakerAmps,
                    Phase = request.Phase,
                    EnclosureTypeId = request.EnclosureTypeId,
                    RuleSetId = request.RuleSetId,
                    Notes = request.Notes,
                    LocationId = request.LocationId,
                    HasIsolator = request.HasIsolator ?? true,
                    TerminalsAtBottom = request.TerminalsAtBottom ?? false,
                    LayoutVersion = 0,
                    Circuits = ToRows(request.Circuits),
                    ExtraFixtures = (request.ExtraFixtures ?? [])
                        .Where(f => f.Quantity > 0)
                        .Select((f, i) => new ExtraFixtureRow
                        {
                            Id = Guid.NewGuid(),
                            DeviceTypeId = f.DeviceTypeId,
                            Quantity = f.Quantity,
                            Sequence = i
                        }).ToList()
                };

                // One transaction for the save and the layout: leaving this block
                // without committing — a refused design, an exception — undoes
                // the insert too, so the house never gains a submain with no panel.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                db.Submains.Add(submain);
                await db.SaveChangesAsync(ct);

                if (request.Generate == true)
                {
                    var refusal = Refusal(await designs.GenerateAsync(submain.Id, null, ct));
                    if (refusal is not null) return refusal;
                }

                await transaction.CommitAsync(ct);
                return Results.Created($"/submains/{submain.Id}", await Load(db, submain.Id, ct));
            })
            .WithTags("Submains");

        app.MapGet("/submains/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                var submain = await Load(db, id, ct);
                return submain is null ? Results.NotFound() : Results.Ok(submain);
            })
            .WithTags("Submains");

        // Sequence order, the order the build form groups them in. Terminal
        // order is the terminal sheet's business (GET /terminals).
        app.MapGet("/submains/{id:guid}/circuits", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                if (!await db.Submains.AnyAsync(s => s.Id == id, ct)) return Results.NotFound();

                var circuits = await db.Circuits
                    .Where(c => c.SubmainId == id)
                    .OrderBy(c => c.Sequence)
                    .Select(c => new CircuitResponse(
                        c.Id, c.Type, c.Name, c.Room, c.Sequence, c.WattsPerMetre, c.LengthMetres))
                    .ToListAsync(ct);

                return Results.Ok(circuits);
            })
            .WithTags("Submains");

        app.MapPatch("/submains/{id:guid}", async (
                Guid id,
                UpdateSubmainRequest request,
                HouseConfigDbContext db,
                DesignService designs,
                CancellationToken ct) =>
            {
                var submain = await db.Submains
                    .Include(s => s.Circuits)
                    .Include(s => s.ExtraFixtures)
                    .SingleOrDefaultAsync(s => s.Id == id, ct);
                if (submain is null) return Results.NotFound();

                var problems = Validate(request.Name, request.Circuits);
                if (await LocationRules.ProblemAsync(db, submain.ProjectId, request.LocationId, ct) is { } misplaced)
                {
                    problems["locationId"] = [misplaced];
                }
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // As on create: the edit and the rebuild stand or fall together.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                submain.Name = request.Name.Trim();
                submain.Reference = request.Reference;
                submain.FeedCableSize = request.FeedCableSize;
                submain.OriginBreakerAmps = request.OriginBreakerAmps;
                submain.Phase = request.Phase;
                submain.EnclosureTypeId = request.EnclosureTypeId;
                submain.RuleSetId = request.RuleSetId;
                submain.Notes = request.Notes;
                submain.LocationId = request.LocationId;
                submain.HasIsolator = request.HasIsolator ?? submain.HasIsolator;
                submain.TerminalsAtBottom = request.TerminalsAtBottom ?? submain.TerminalsAtBottom;

                if (request.Circuits is not null)
                {
                    ApplyCircuits(db, submain, ToRows(request.Circuits));
                }

                if (request.ExtraFixtures is not null)
                {
                    ApplyExtraFixtures(db, submain, request.ExtraFixtures);
                }

                await db.SaveChangesAsync(ct);

                if (request.Rebuild == true)
                {
                    var refusal = Refusal(await designs.GenerateAsync(id, null, ct));
                    if (refusal is not null) return refusal;
                }

                await transaction.CommitAsync(ct);
                return Results.Ok(await Load(db, id, ct));
            })
            .WithTags("Submains");

        return app;
    }

    /// The same answers POST /design/generate gives when it stores nothing — a
    /// validation problem for what the user can fix, the refused design with its
    /// diagnostics as a 422 — or null when the panel was built. The caller's
    /// transaction is still open, so returning the refusal rolls the edit back.
    internal static IResult? Refusal((DesignResponse? Response, string? Error, bool HasErrors) generated)
    {
        var (response, error, hasErrors) = generated;
        if (error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
        }

        return hasErrors
            ? Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity)
            : null;
    }

    /// Replaces the submain's circuit list wholesale. Circuits the caller sends
    /// with an id keep that id, so their names and any channel wiring survive.
    internal static void ApplyCircuits(HouseConfigDbContext db, Submain submain, List<CircuitRow> incoming)
    {
        var incomingIds = incoming.Select(c => c.Id).ToHashSet();

        db.Circuits.RemoveRange(submain.Circuits.Where(c => !incomingIds.Contains(c.Id)));

        foreach (var row in incoming)
        {
            var existing = submain.Circuits.SingleOrDefault(c => c.Id == row.Id);
            if (existing is null)
            {
                row.SubmainId = submain.Id;
                db.Circuits.Add(row);
            }
            else
            {
                existing.Type = row.Type;
                existing.Name = row.Name;
                existing.Room = row.Room;
                existing.Sequence = row.Sequence;
                existing.WattsPerMetre = row.WattsPerMetre;
                existing.LengthMetres = row.LengthMetres;
            }
        }
    }

    internal static Dictionary<string, string[]> Validate(string? name, IReadOnlyList<CircuitRequest>? circuits)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            problems["name"] = ["An automation panel name is required."];
        }

        if (circuits is null) return problems;

        foreach (var (circuit, index) in circuits.Select((c, i) => (c, i)))
        {
            if (!Enum.TryParse<CircuitType>(circuit.Type, out _))
            {
                problems[$"circuits[{index}].type"] =
                    [$"'{circuit.Type}' is not a circuit type. Use DimmedLighting, Switched or LedTape."];
            }

            if (string.IsNullOrWhiteSpace(circuit.Name))
            {
                problems[$"circuits[{index}].name"] = ["A circuit name is required."];
            }
        }

        return problems;
    }

    internal static List<CircuitRow> ToRows(IReadOnlyList<CircuitRequest>? circuits)
        => (circuits ?? []).Select(c => new CircuitRow
        {
            Id = c.Id ?? Guid.NewGuid(),
            Type = c.Type,
            Name = c.Name.Trim(),
            Room = c.Room,
            Sequence = c.Sequence,
            WattsPerMetre = c.WattsPerMetre,
            LengthMetres = c.LengthMetres
        }).ToList();

    /// Replaces the fixture list wholesale, like the circuits. There is nothing
    /// on a fixture worth preserving across an edit — no name, no wiring — so the
    /// rows are simply rebuilt.
    internal static void ApplyExtraFixtures(
        HouseConfigDbContext db, Submain submain, IReadOnlyList<ExtraFixtureRequest> incoming)
    {
        db.ExtraFixtures.RemoveRange(submain.ExtraFixtures);
        submain.ExtraFixtures.Clear();

        var sequence = 0;
        foreach (var fixture in incoming.Where(f => f.Quantity > 0))
        {
            // Through the DbSet, not the navigation: a row that arrives through
            // the navigation with its Guid already set is taken for an existing
            // one and UPDATEd — zero rows, and the whole edit fails with a 500.
            db.ExtraFixtures.Add(new ExtraFixtureRow
            {
                Id = Guid.NewGuid(),
                SubmainId = submain.Id,
                DeviceTypeId = fixture.DeviceTypeId,
                Quantity = fixture.Quantity,
                Sequence = sequence++
            });
        }
    }

    private static async Task<SubmainResponse?> Load(HouseConfigDbContext db, Guid id, CancellationToken ct)
        => await db.Submains.Where(s => s.Id == id).Select(ToResponse).SingleOrDefaultAsync(ct);

    /// An Expression, not a method: EF Core cannot translate a method call inside Select.
    private static readonly Expression<Func<Submain, SubmainResponse>> ToResponse = s => new SubmainResponse(
        s.Id, s.ProjectId, s.Name, s.Reference, s.FeedCableSize, s.OriginBreakerAmps, s.Phase,
        s.EnclosureTypeId, s.RuleSetId, s.Notes, s.HasIsolator, s.TerminalsAtBottom, s.LayoutVersion,
        s.Circuits.Count, s.Devices.Count,
        s.ExtraFixtures.OrderBy(f => f.Sequence)
            .Select(f => new ExtraFixtureResponse(f.DeviceTypeId, f.Quantity)).ToList(),
        s.LocationId);
}
