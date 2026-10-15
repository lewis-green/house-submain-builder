using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Racks;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackItemEndpoints
{
    /// The longest port label the schedule's column holds without wrapping badly.
    public const int MaxPortLabel = 60;

    /// Adds racing for the same rack are placed again against what the winner
    /// left, up to this many times, before the loser is told to reload.
    private const int MaxAddAttempts = 3;

    /// A port save that loses a race is run once more against what won.
    private const int MaxPortSaveAttempts = 2;

    public static IEndpointRouteBuilder MapRackItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/racks/{id:guid}/items", async (
                Guid id,
                AddRackItemRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var typeRow = await db.RackDeviceTypes.SingleOrDefaultAsync(t => t.Id == request.RackDeviceTypeId, ct);
                if (typeRow is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["rackDeviceTypeId"] = ["Choose an item from the catalogue."]
                    });
                }

                var type = DomainMapper.ToDomain(typeRow);
                if (type.Kind == RackDeviceKind.Accessory)
                {
                    return Refuse($"{type.Description} is an accessory. It goes on the parts list with the item that needs it, not in the rack.");
                }

                if (!type.Active) return Refuse($"{type.Description} is no longer offered in the catalogue.");

                for (var attempt = 1; ; attempt++)
                {
                    var rack = await db.Racks.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
                    if (rack is null) return Results.NotFound();

                    var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
                    var spans = await RackViews.SpansAsync(db, rack, ct);

                    if (RackPlacement.NextTopU(cabinet.HeightU, type.HeightU, spans) is not { } topU)
                    {
                        return Refuse(
                            $"No room in this rack: {RackPlacement.FreeU(cabinet.HeightU, spans)}U free, " +
                            $"and {type.Description} needs {type.HeightU}U.");
                    }

                    // Added through the set, not just rack.Items: with its key set
                    // by hand, an item found through the navigation would be tracked
                    // as an existing row to update, and the update of a row that is
                    // not there reads as a concurrency failure.
                    db.RackItems.Add(new RackItem
                    {
                        Id = Guid.NewGuid(),
                        RackId = rack.Id,
                        RackDeviceTypeId = type.Id,
                        TopU = topU,
                        Sequence = rack.Items.Count == 0 ? 1 : rack.Items.Max(i => i.Sequence) + 1,
                        Label = NextLabel(rack, type.Kind),
                    });
                    rack.LayoutVersion++;

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Created($"/racks/{rack.Id}/layout", await RackViews.LayoutAsync(db, rack.Id, ct));
                    }
                    // Another add saved first: the rack's version moved on (a
                    // concurrency failure) or it took the same U (the unique
                    // index). Either way, place again against what is there now.
                    catch (DbUpdateException) when (attempt < MaxAddAttempts)
                    {
                        db.ChangeTracker.Clear();
                    }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        return Conflict(await db.Racks.Where(r => r.Id == id).Select(r => r.LayoutVersion).SingleAsync(ct));
                    }
                }
            })
            .WithTags("Racks");

        app.MapPatch("/rack-items/{id:guid}/position", async (
                Guid id,
                MoveRackItemRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var rack = await db.Racks.Include(r => r.Items).SingleAsync(r => r.Id == item.RackId, ct);
                if (rack.LayoutVersion != request.BasedOnLayoutVersion) return Conflict(rack.LayoutVersion);

                var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
                var spans = await RackViews.SpansAsync(db, rack, ct);
                var height = spans.Single(s => s.ItemId == id).HeightU;
                var bottom = request.TopU - height + 1;

                if (request.TopU > cabinet.HeightU || bottom < 1)
                {
                    return Refuse($"U{request.TopU} is outside this {cabinet.HeightU}U cabinet.");
                }

                if (!RackPlacement.CanPlace(cabinet.HeightU, request.TopU, height, spans, id))
                {
                    return Refuse(height == 1 ? $"U{request.TopU} is taken." : $"U{bottom}–U{request.TopU} is not free.");
                }

                item.TopU = request.TopU;
                rack.LayoutVersion++;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    return Conflict(await db.Racks.Where(r => r.Id == rack.Id).Select(r => r.LayoutVersion).SingleAsync(ct));
                }

                return Results.Ok(new { rack.LayoutVersion });
            })
            .WithTags("Racks");

        app.MapDelete("/rack-items/{id:guid}", async (
                Guid id,
                int basedOnLayoutVersion,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var rack = await db.Racks.SingleAsync(r => r.Id == item.RackId, ct);
                if (rack.LayoutVersion != basedOnLayoutVersion) return Conflict(rack.LayoutVersion);

                // Its ports go with it, by the cascade on RackPorts. Uplinks into
                // it, from this rack or another, are cleared in the same save: a
                // link to a switch that is gone would say where no cable goes.
                // Under the house's uplink lock, so no uplink save lands in between.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await RackUplinks.LockHouseAsync(db, rack.ProjectId, ct);
                await RackUplinks.ClearIntoAsync(db, id, ct);
                db.RackItems.Remove(item);
                rack.LayoutVersion++;

                try
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    return Conflict(await db.Racks.Where(r => r.Id == rack.Id).Select(r => r.LayoutVersion).SingleAsync(ct));
                }

                return Results.Ok(await RackViews.LayoutAsync(db, rack.Id, ct));
            })
            .WithTags("Racks");

        // Port edits leave the layout version alone, as circuit names do: they
        // move nothing. They still make an issued PDF out of date, which the
        // revision's snapshot comparison sees.
        app.MapPut("/rack-items/{id:guid}/ports/{number:int}", async (
                Guid id,
                int number,
                SaveRackPortRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var item = await db.RackItems.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return Results.NotFound();

                var type = DomainMapper.ToDomain(await db.RackDeviceTypes.SingleAsync(t => t.Id == item.RackDeviceTypeId, ct));
                var group = type.GroupOf(number);
                if (group is null) return Results.NotFound();

                var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
                if (label is { Length: > MaxPortLabel })
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["label"] = [$"Keep a port label to {MaxPortLabel} characters; this one is {label.Length}."]
                    });
                }

                if (request.Poe && !group.PoeTick)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["poe"] = [$"Port {number} is an {group.Name} port, which cannot supply PoE."]
                    });
                }

                // Two saves of a port not yet stored can both find nothing and
                // both insert it, and the second insert hits the port's key; or
                // one save clears the port while another updates it, and the
                // update finds no row. The loser is run once more against what
                // the winner left, and lands where a save made a moment later
                // would have.
                for (var attempt = 1; ; attempt++)
                {
                    var port = await db.RackPorts.SingleOrDefaultAsync(p => p.RackItemId == id && p.Number == number, ct);

                    if (label is null && !request.Poe)
                    {
                        // An empty port is not stored at all.
                        if (port is not null) db.RackPorts.Remove(port);
                    }
                    else if (port is null)
                    {
                        db.RackPorts.Add(new RackPort { RackItemId = id, Number = number, Label = label, Poe = request.Poe });
                    }
                    else
                    {
                        port.Label = label;
                        port.Poe = request.Poe;
                    }

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Ok(new RackPortResponse(number, label, request.Poe));
                    }
                    catch (DbUpdateException) when (attempt < MaxPortSaveAttempts)
                    {
                        db.ChangeTracker.Clear();
                    }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        return Results.Json(
                            new ConflictResponse(
                                $"Port {number} was saved by someone else at the same moment. Reload and try again.",
                                await db.Racks.Where(r => r.Id == item.RackId).Select(r => r.LayoutVersion).SingleAsync(ct)),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                }
            })
            .WithTags("Racks");

        return app;
    }

    private static string NextLabel(Rack rack, RackDeviceKind kind) => kind switch
    {
        RackDeviceKind.Switch => $"Switch {++rack.SwitchesNamed}",
        RackDeviceKind.PatchPanel => $"Patch panel {++rack.PatchPanelsNamed}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only switches and patch panels are placed."),
    };

    private static IResult Refuse(string message) =>
        Results.Json(new RefusalResponse(message), statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult Conflict(int currentLayoutVersion) =>
        Results.Json(
            new ConflictResponse("This rack changed since you loaded it. Reload and try again.", currentLayoutVersion),
            statusCode: StatusCodes.Status409Conflict);
}
