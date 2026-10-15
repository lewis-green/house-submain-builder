using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackUplinkEndpoints
{
    /// An uplink save that loses a race is run once more against what won.
    private const int MaxUplinkSaveAttempts = 2;

    public static IEndpointRouteBuilder MapRackUplinkEndpoints(this IEndpointRouteBuilder app)
    {
        // Uplinks leave the layout version alone, as port labels do: they move
        // nothing. Both racks' issued PDFs print them, which the revisions'
        // snapshot comparison sees.
        app.MapPut("/rack-items/{id:guid}/uplink", async (
                Guid id,
                SaveRackUplinkRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                // Two switches saved into the same port at once can both pass the
                // check, and the index refuses the second; or the switch chosen
                // is removed between the check and the save. The loser is checked
                // again against what the winner left, and told what a save made
                // a moment later would have been told.
                for (var attempt = 1; ; attempt++)
                {
                    var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                    if (item is null) return Results.NotFound();

                    var placed = await RackUplinks.Where(db, db.RackItems.Where(i => i.Id == id)).SingleAsync(ct);
                    var type = DomainMapper.ToDomain(await db.RackDeviceTypes.SingleAsync(t => t.Id == item.RackDeviceTypeId, ct));
                    var (problems, text) = await RackUplinks.ValidateAsync(db, placed, type, request, ct);
                    if (problems.Count > 0) return Results.ValidationProblem(problems);

                    item.UplinkPort = request.Port;
                    item.UplinkToItemId = text is null ? request.ToItemId : null;
                    item.UplinkToPort = text is null ? request.ToPort : null;
                    item.UplinkToText = text;

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Ok(await RackViews.LayoutAsync(db, item.RackId, ct));
                    }
                    // Also a removal of the item itself, which the next attempt finds.
                    catch (DbUpdateException) when (attempt < MaxUplinkSaveAttempts)
                    {
                        db.ChangeTracker.Clear();
                    }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        return Results.Json(
                            new ConflictResponse(
                                $"{item.Label}'s uplink was saved by someone else at the same moment. Reload and try again.",
                                await db.Racks.Where(r => r.Id == item.RackId).Select(r => r.LayoutVersion).SingleAsync(ct)),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                }
            })
            .WithTags("Racks");

        // Removing one that is already gone is no error: a switch it went to
        // may have been removed in another rack, clearing it, since this
        // screen loaded.
        app.MapDelete("/rack-items/{id:guid}/uplink", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                RackUplinks.Clear(item);

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // The item itself was removed after it was read.
                    return Results.NotFound();
                }

                return Results.Ok(await RackViews.LayoutAsync(db, item.RackId, ct));
            })
            .WithTags("Racks");

        // Every switch in the house, for the uplink picker: the current one
        // included, since which one is current is the screen's business.
        // Placed racks first, in the order their locations were added, as the
        // house page lists them; then by rack name, and top U first.
        app.MapGet("/projects/{id:guid}/switches", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
            {
                if (!await db.Projects.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

                var items = await RackUplinks.Where(db, db.RackItems.Where(i => db.Racks.Any(r => r.Id == i.RackId && r.ProjectId == id)))
                    .ToListAsync(ct);
                var typeIds = items.Select(i => i.RackDeviceTypeId).Distinct().ToList();
                var types = (await db.RackDeviceTypes.Where(t => typeIds.Contains(t.Id)).ToListAsync(ct))
                    .Select(DomainMapper.ToDomain)
                    .ToDictionary(t => t.Id);

                return Results.Ok(items
                    .Where(i => types[i.RackDeviceTypeId].Kind == RackDeviceKind.Switch)
                    .OrderBy(i => i.LocationSequence is null)
                    .ThenBy(i => i.LocationSequence)
                    .ThenBy(i => i.RackName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.RackId)
                    .ThenByDescending(i => i.TopU)
                    .Select(i => new HouseSwitchResponse(
                        i.Id, i.Label, types[i.RackDeviceTypeId].Description, i.RackId, i.RackName, i.LocationId, i.LocationName,
                        types[i.RackDeviceTypeId].PortGroups,
                        // Only a link that still goes somewhere holds a port.
                        i.UplinkToItemId is not null || i.UplinkToPort is null ? i.UplinkPort : null,
                        items
                            .Where(s => s.UplinkToItemId == i.Id && s.UplinkToPort is not null)
                            .OrderBy(s => s.UplinkToPort)
                            .Select(s => new RackDownlinkResponse(s.UplinkToPort!.Value, s.Id, s.Label, s.RackId, s.RackName))
                            .ToList()))
                    .ToList());
            })
            .WithTags("Racks");

        return app;
    }
}
