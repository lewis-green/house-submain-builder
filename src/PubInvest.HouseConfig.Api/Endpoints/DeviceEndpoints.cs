using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/devices/{id:guid}/channels/{index:int}", async (
                Guid id,
                int index,
                UpdateChannelRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var device = await db.DeviceInstances.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null) return Results.NotFound();

                var submain = await db.Submains.SingleAsync(s => s.Id == device.SubmainId, ct);
                if (submain.LayoutVersion != request.BasedOnLayoutVersion)
                {
                    return Results.Json(
                        new ConflictResponse(
                            "This panel changed since you loaded it. Reload and try again.",
                            submain.LayoutVersion),
                        statusCode: StatusCodes.Status409Conflict);
                }

                var channel = await db.DeviceChannels
                    .SingleOrDefaultAsync(c => c.DeviceInstanceId == id && c.ChannelIndex == index, ct);
                if (channel is null) return Results.NotFound();

                if (request.CircuitId is { } circuitId)
                {
                    var circuit = await db.Circuits
                        .SingleOrDefaultAsync(c => c.Id == circuitId && c.SubmainId == device.SubmainId, ct);

                    if (circuit is null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["circuitId"] = ["That circuit is not on this submain."]
                        });
                    }

                    var alreadyElsewhere = await db.DeviceChannels
                        .Where(c => c.CircuitId == circuitId)
                        .AnyAsync(c => c.DeviceInstanceId != id || c.ChannelIndex != index, ct);

                    if (alreadyElsewhere)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["circuitId"] = ["That circuit is already wired to another channel. Free it first."]
                        });
                    }
                }

                channel.CircuitId = request.CircuitId;

                // Derived, not trusted from the request: a channel with no circuit
                // that is not spare would be a state with no meaning.
                channel.IsSpare = request.CircuitId is null;

                submain.LayoutVersion++;
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { submain.LayoutVersion });
            })
            .WithTags("Devices");

        return app;
    }
}
