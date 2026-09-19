using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class CircuitEndpoints
{
    public static IEndpointRouteBuilder MapCircuitEndpoints(this IEndpointRouteBuilder app)
    {
        // Renaming changes no geometry, so it carries no layoutVersion and never
        // conflicts with someone renaming a different circuit.
        //
        // Name and room are replaced together, as one edit: the device sheet shows
        // both fields and always sends both. A caller that sends only a name
        // clears the room.
        app.MapPatch("/circuits/{id:guid}", async (
                Guid id,
                UpdateCircuitRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["name"] = ["A circuit name is required."]
                    });
                }

                var circuit = await db.Circuits.SingleOrDefaultAsync(c => c.Id == id, ct);
                if (circuit is null) return Results.NotFound();

                circuit.Name = request.Name.Trim();
                circuit.Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim();
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { circuit.Id, circuit.Name, circuit.Room });
            })
            .WithTags("Circuits");

        return app;
    }
}
