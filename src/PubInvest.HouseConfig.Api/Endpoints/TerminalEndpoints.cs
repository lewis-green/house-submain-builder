using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class TerminalEndpoints
{
    public static IEndpointRouteBuilder MapTerminalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/submains/{id:guid}/terminals", async (
                Guid id,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var response = await Load(db, id, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithTags("Terminals");

        // Order and counts are saved together and the panel is rebuilt in the
        // same transaction, as an install option is: extra terminals take rail
        // space, and a saved order over an unchanged drawing is a state nobody
        // can see. A refused rebuild rolls the save back.
        app.MapPut("/submains/{id:guid}/terminals", async (
                Guid id,
                UpdateTerminalsRequest request,
                HouseConfigDbContext db,
                DesignService designs,
                CancellationToken ct) =>
            {
                var submain = await db.Submains
                    .Include(s => s.Circuits)
                    .SingleOrDefaultAsync(s => s.Id == id, ct);
                if (submain is null) return Results.NotFound();

                if (submain.LayoutVersion != request.LayoutVersion)
                {
                    return Results.Json(
                        new ConflictResponse(
                            "This panel changed since you loaded it. Reload and try again.",
                            submain.LayoutVersion),
                        statusCode: StatusCodes.Status409Conflict);
                }

                var problem = Validate(request.Circuits, submain.Circuits.Select(c => c.Id).ToHashSet());
                if (problem is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["circuits"] = [problem] });
                }

                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var byId = submain.Circuits.ToDictionary(c => c.Id);
                foreach (var (entry, index) in request.Circuits!.Select((c, i) => (c, i)))
                {
                    byId[entry.CircuitId].TerminalPosition = index + 1;
                    byId[entry.CircuitId].TerminalCount = entry.TerminalCount;
                }

                await db.SaveChangesAsync(ct);

                var refusal = SubmainEndpoints.Refusal(await designs.GenerateAsync(id, null, ct));
                if (refusal is not null) return refusal;

                await transaction.CommitAsync(ct);
                return Results.Ok(await Load(db, id, ct));
            })
            .WithTags("Terminals");

        return app;
    }

    private static string? Validate(IReadOnlyList<TerminalCircuitRequest>? circuits, HashSet<Guid> known)
    {
        if (circuits is null || circuits.Count != known.Count
            || circuits.Select(c => c.CircuitId).Distinct().Count() != circuits.Count
            || circuits.Any(c => !known.Contains(c.CircuitId)))
        {
            return "List every circuit on this submain exactly once.";
        }

        if (circuits.Any(c => c.TerminalCount is < 1 or > TerminalBandBuilder.MaxTerminalsPerCircuit))
        {
            return $"A circuit lands on 1 to {TerminalBandBuilder.MaxTerminalsPerCircuit} terminals.";
        }

        return null;
    }

    private static async Task<TerminalsResponse?> Load(HouseConfigDbContext db, Guid id, CancellationToken ct)
    {
        var submain = await db.Submains
            .AsNoTracking()
            .Include(s => s.Circuits)
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (submain is null) return null;

        var rows = submain.Circuits.ToDictionary(c => c.Id);
        var ordered = TerminalBandBuilder.InTerminalOrder(submain.Circuits.Select(DomainMapper.ToDomain));

        return new TerminalsResponse(
            submain.LayoutVersion,
            ordered.Select(c => new TerminalCircuitResponse(
                c.Id, c.Name, c.Room, rows[c.Id].Type, c.TerminalCount)).ToList());
    }
}
