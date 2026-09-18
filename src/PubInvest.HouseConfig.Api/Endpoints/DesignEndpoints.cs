using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class DesignEndpoints
{
    public static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/submains/{id:guid}/design/preview", async (
                Guid id,
                PreviewRequest? request,
                DesignService service,
                CancellationToken ct) =>
            {
                var (inputs, error) = await service.LoadAsync(id, request, ct);
                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
                }

                if (inputs is null) return Results.NotFound();

                var result = PanelGenerator.Generate(inputs.Request);

                return Results.Ok(DesignService.ToResponse(
                    id, inputs.Submain.LayoutVersion, result, inputs.Circuits));
            })
            .WithTags("Design");

        app.MapPost("/submains/{id:guid}/design/generate", async (
                Guid id,
                PreviewRequest? request,
                DesignService service,
                CancellationToken ct) =>
            {
                var (response, error, hasErrors) = await service.GenerateAsync(id, request, ct);
                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
                }

                if (response is null) return Results.NotFound();

                return hasErrors
                    ? Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity)
                    : Results.Ok(response);
            })
            .WithTags("Design");

        // The panel screen loads this, not /design/preview: preview regenerates
        // from scratch and returns id: null on every device, which is right for a
        // wizard and useless for editing.
        app.MapGet("/submains/{id:guid}/layout", async (
                Guid id,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var submain = await db.Submains
                    .Include(s => s.Circuits)
                    .SingleOrDefaultAsync(s => s.Id == id, ct);

                if (submain is null) return Results.NotFound();

                var devices = await db.DeviceInstances
                    .Include(d => d.Channels)
                    .Where(d => d.SubmainId == id)
                    .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
                    .ToListAsync(ct);

                var enclosure = submain.EnclosureTypeId is null
                    ? null
                    : await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);

                var names = submain.Circuits.ToDictionary(c => c.Id, c => c.Name);

                var placed = devices.Select(d => new PlacedDeviceResponse(
                    d.Id,
                    d.DeviceTypeId,
                    d.Category,
                    d.RowIndex,
                    d.StartSlot,
                    d.ModuleWidth,
                    d.Label,
                    d.TerminalRole,
                    d.Channels.OrderBy(c => c.ChannelIndex).Select(c => new ChannelResponse(
                        c.ChannelIndex,
                        c.CircuitId,
                        c.CircuitId is not null && names.TryGetValue(c.CircuitId.Value, out var n) ? n : null,
                        c.IsSpare)).ToList())).ToList();

                var rowsUsed = placed.Count == 0 ? 0 : placed.Max(d => d.RowIndex) + 1;
                var rows = enclosure?.Rows ?? 0;
                var slotsPerRow = enclosure?.SlotsPerRow ?? 0;

                // Diagnostics and BOM are products of *generating*. A stored layout
                // is the record of a generation that already happened, so it carries
                // neither; the panel screen shows what that generate call returned.
                return Results.Ok(new DesignResponse(
                    id,
                    submain.LayoutVersion,
                    new LayoutResponse(rows, slotsPerRow, placed),
                    [],
                    [],
                    new DesignSummary(
                        rowsUsed,
                        placed.Sum(d => d.ModuleWidth),
                        rows * slotsPerRow,
                        placed.Count,
                        placed.SelectMany(d => d.Channels).Count(c => c.IsSpare))));
            })
            .WithTags("Design");

        return app;
    }
}
