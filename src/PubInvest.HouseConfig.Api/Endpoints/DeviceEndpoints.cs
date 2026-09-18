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

        app.MapPatch("/devices/{id:guid}/position", async (
                Guid id,
                UpdatePositionRequest request,
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

                var enclosure = submain.EnclosureTypeId is null
                    ? null
                    : await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);

                if (enclosure is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["design"] = ["This submain has no enclosure."]
                    });
                }

                var end = request.StartSlot + device.ModuleWidth;

                if (request.RowIndex < 0 || request.RowIndex >= enclosure.Rows
                    || request.StartSlot < 0 || end > enclosure.SlotsPerRow)
                {
                    return Results.Json(
                        new { message = "That position is outside the enclosure." },
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                var blocked = await db.DeviceInstances.AnyAsync(other =>
                    other.SubmainId == device.SubmainId
                    && other.Id != device.Id
                    && other.RowIndex == request.RowIndex
                    && other.StartSlot < end
                    && request.StartSlot < other.StartSlot + other.ModuleWidth, ct);

                if (blocked)
                {
                    return Results.Json(
                        new { message = "Another device is already in that space." },
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                device.RowIndex = request.RowIndex;
                device.StartSlot = request.StartSlot;

                var existing = await db.PositionOverrides
                    .SingleOrDefaultAsync(o => o.SubmainId == device.SubmainId && o.Label == device.Label, ct);

                if (existing is null)
                {
                    db.PositionOverrides.Add(new Data.Entities.PositionOverrideRow
                    {
                        Id = Guid.NewGuid(),
                        SubmainId = device.SubmainId,
                        Label = device.Label,
                        RowIndex = request.RowIndex,
                        StartSlot = request.StartSlot
                    });
                }
                else
                {
                    existing.RowIndex = request.RowIndex;
                    existing.StartSlot = request.StartSlot;
                }

                submain.LayoutVersion++;
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { submain.LayoutVersion, device.RowIndex, device.StartSlot });
            })
            .WithTags("Devices");

        return app;
    }
}
