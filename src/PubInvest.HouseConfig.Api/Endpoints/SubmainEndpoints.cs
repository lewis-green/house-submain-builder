using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
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
                CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return Results.NotFound();

                var problems = Validate(request.Name, request.Circuits);
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
                    HasIsolator = request.HasIsolator ?? true,
                    TerminalsAtBottom = request.TerminalsAtBottom ?? false,
                    LayoutVersion = 0,
                    Circuits = ToRows(request.Circuits)
                };

                db.Submains.Add(submain);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/submains/{submain.Id}", await Load(db, submain.Id, ct));
            })
            .WithTags("Submains");

        app.MapGet("/submains/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                var submain = await Load(db, id, ct);
                return submain is null ? Results.NotFound() : Results.Ok(submain);
            })
            .WithTags("Submains");

        app.MapPatch("/submains/{id:guid}", async (
                Guid id,
                UpdateSubmainRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var submain = await db.Submains.Include(s => s.Circuits).SingleOrDefaultAsync(s => s.Id == id, ct);
                if (submain is null) return Results.NotFound();

                var problems = Validate(request.Name, request.Circuits);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                submain.Name = request.Name.Trim();
                submain.Reference = request.Reference;
                submain.FeedCableSize = request.FeedCableSize;
                submain.OriginBreakerAmps = request.OriginBreakerAmps;
                submain.Phase = request.Phase;
                submain.EnclosureTypeId = request.EnclosureTypeId;
                submain.RuleSetId = request.RuleSetId;
                submain.Notes = request.Notes;
                submain.HasIsolator = request.HasIsolator ?? submain.HasIsolator;
                submain.TerminalsAtBottom = request.TerminalsAtBottom ?? submain.TerminalsAtBottom;

                if (request.Circuits is not null)
                {
                    ApplyCircuits(db, submain, ToRows(request.Circuits));
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(await Load(db, id, ct));
            })
            .WithTags("Submains");

        return app;
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
            problems["name"] = ["A submain name is required."];
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

    private static async Task<SubmainResponse?> Load(HouseConfigDbContext db, Guid id, CancellationToken ct)
        => await db.Submains.Where(s => s.Id == id).Select(ToResponse).SingleOrDefaultAsync(ct);

    /// An Expression, not a method: EF Core cannot translate a method call inside Select.
    private static readonly Expression<Func<Submain, SubmainResponse>> ToResponse = s => new SubmainResponse(
        s.Id, s.ProjectId, s.Name, s.Reference, s.FeedCableSize, s.OriginBreakerAmps, s.Phase,
        s.EnclosureTypeId, s.RuleSetId, s.Notes, s.HasIsolator, s.TerminalsAtBottom, s.LayoutVersion,
        s.Circuits.Count, s.Devices.Count);
}
