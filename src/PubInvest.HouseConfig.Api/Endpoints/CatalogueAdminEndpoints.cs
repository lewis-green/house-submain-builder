using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class CatalogueAdminEndpoints
{
    public static IEndpointRouteBuilder MapCatalogueAdminEndpoints(this IEndpointRouteBuilder app, bool authEnabled)
    {
        var group = app.MapGroup("/catalogue").WithTags("Catalogue admin");
        if (authEnabled) group.RequireAuthorization("catalogue-admin");

        group.MapPost("/device-types", async (
                SaveDeviceTypeRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var problems = ValidateDeviceType(request);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                if (await db.DeviceTypes.AnyAsync(d => d.PartNumber == request.PartNumber, ct))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["partNumber"] = [$"'{request.PartNumber}' is already in the catalogue."]
                    });
                }

                var row = new DeviceTypeRow { Id = Guid.NewGuid() };
                Apply(row, request);
                db.DeviceTypes.Add(row);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/catalogue/device-types/{row.Id}", DomainMapper.ToDomain(row));
            });

        group.MapPatch("/device-types/{id:guid}", async (
                Guid id,
                SaveDeviceTypeRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var row = await db.DeviceTypes.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (row is null) return Results.NotFound();

                var problems = ValidateDeviceType(request);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                // Deactivating something a ruleset still points at would only fail
                // later, at generation time, on site.
                if (!request.Active && row.Active)
                {
                    var blocking = await ReferencingRuleSet(db, id, ct);
                    if (blocking is not null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["active"] = [$"Ruleset '{blocking}' still uses this device. Point it elsewhere first."]
                        });
                    }
                }

                Apply(row, request);
                await db.SaveChangesAsync(ct);

                return Results.Ok(DomainMapper.ToDomain(row));
            });

        group.MapPost("/enclosures", async (
                SaveEnclosureRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var problems = ValidateEnclosure(request);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                var row = new EnclosureTypeRow { Id = Guid.NewGuid() };
                Apply(row, request);
                db.Enclosures.Add(row);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/catalogue/enclosures/{row.Id}", DomainMapper.ToDomain(row));
            });

        group.MapPatch("/enclosures/{id:guid}", async (
                Guid id,
                SaveEnclosureRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var row = await db.Enclosures.SingleOrDefaultAsync(e => e.Id == id, ct);
                if (row is null) return Results.NotFound();

                var problems = ValidateEnclosure(request);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                Apply(row, request);
                await db.SaveChangesAsync(ct);

                return Results.Ok(DomainMapper.ToDomain(row));
            });

        group.MapPost("/rulesets", async (
                SaveRuleSetRequest request,
                HouseConfigDbContext db,
                CancellationToken ct) =>
            {
                var problems = await ValidateRuleSet(request, db, ct);
                if (problems.Count > 0) return Results.ValidationProblem(problems);

                if (await db.RuleSets.AnyAsync(r => r.Name == request.Name && r.Version == request.Version, ct))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["version"] = [$"'{request.Name}' version {request.Version} already exists."]
                    });
                }

                if (request.IsDefault)
                {
                    await ClearDefaults(db, ct);
                }

                var row = new RuleSetRow
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name.Trim(),
                    Version = request.Version,
                    IsDefault = request.IsDefault,
                    PayloadJson = JsonSerializer.Serialize(request.Payload, DomainMapper.Json),
                };

                db.RuleSets.Add(row);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/catalogue/rulesets/{row.Id}", new
                {
                    row.Id, row.Name, row.Version, row.IsDefault,
                });
            });

        return app;
    }

    private static async Task<string?> ReferencingRuleSet(HouseConfigDbContext db, Guid deviceTypeId, CancellationToken ct)
    {
        foreach (var row in await db.RuleSets.ToListAsync(ct))
        {
            if (Referenced(DomainMapper.ToDomain(row)).Contains(deviceTypeId)) return row.Name;
        }

        return null;
    }

    private static IEnumerable<Guid> Referenced(RuleSetPayload payload)
    {
        yield return payload.PreferredDevice.Isolator;
        yield return payload.PreferredDevice.Dimmer240;
        yield return payload.PreferredDevice.Dimmer0_10V;
        yield return payload.PreferredDevice.Relay;
        yield return payload.PreferredDevice.Dc24VPositive;
        yield return payload.PreferredDevice.Dc24VNegative;
        foreach (var driver in payload.PreferredDevice.ExternalDriver) yield return driver;
        yield return payload.Terminals.DeviceTypeId;
        yield return payload.Terminals.BridgeBarDeviceTypeId;
        yield return payload.Terminals.EndStopDeviceTypeId;
    }

    private static Dictionary<string, string[]> ValidateDeviceType(SaveDeviceTypeRequest request)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.PartNumber))
        {
            problems["partNumber"] = ["A part number is required."];
        }

        if (!Enum.TryParse<DeviceCategory>(request.Category, out var category))
        {
            problems["category"] = [$"'{request.Category}' is not a device category."];
            return problems;
        }

        if (request.ModuleWidth < 0)
        {
            problems["moduleWidth"] = ["Width cannot be negative."];
        }
        else if (request.ModuleWidth == 0
                 && category is not (DeviceCategory.Accessory or DeviceCategory.ExternalDriver))
        {
            // A zero-width rail device would let the packer fit an unlimited
            // number of them into one row. Accessories and external drivers are
            // never placed, so they are allowed no width.
            problems["moduleWidth"] =
                ["Only accessories and external parts may have no width; a rail device must be at least one slot."];
        }

        if (request.ChannelCount < 0)
        {
            problems["channelCount"] = ["Channel count cannot be negative."];
        }
        else if (request.ChannelCount == 0 &&
                 category is DeviceCategory.Dimmer240 or DeviceCategory.Dimmer0_10V or DeviceCategory.Relay)
        {
            problems["channelCount"] = ["A dimmer or relay must have at least one channel."];
        }

        if (request.Cost < 0m)
        {
            problems["cost"] = ["Cost cannot be negative."];
        }

        return problems;
    }

    private static Dictionary<string, string[]> ValidateEnclosure(SaveEnclosureRequest request)
    {
        var problems = new Dictionary<string, string[]>();

        if (request.Rows <= 0) problems["rows"] = ["An enclosure needs at least one row."];

        if (request.SlotsPerRow <= 0)
        {
            problems["slotsPerRow"] = ["A row needs at least one slot."];
        }
        else if (request.SlotsPerRow % DinUnits.PerModule != 0)
        {
            // Widths are counted in thirds of a module; a row that is a fraction
            // of a module is not a real enclosure.
            problems["slotsPerRow"] =
                [$"Slots per row must be a multiple of {DinUnits.PerModule}, since {DinUnits.PerModule} slots make one DIN module."];
        }

        if (request.Cost < 0m) problems["cost"] = ["Cost cannot be negative."];

        return problems;
    }

    private static async Task<Dictionary<string, string[]>> ValidateRuleSet(
        SaveRuleSetRequest request,
        HouseConfigDbContext db,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name)) problems["name"] = ["A ruleset name is required."];

        if (request.Payload.PsuDeratingFactor is <= 0m or > 1m)
        {
            problems["psuDeratingFactor"] = ["The derating factor must be greater than 0 and at most 1."];
        }

        // A payload that omits zones deserialises them as null. That must read as
        // "you forgot the zones", not as a 500.
        if (request.Payload.Zones is null || request.Payload.Zones.Count == 0)
        {
            problems["zones"] = ["A ruleset needs at least one packing zone."];
        }
        else if (request.Payload.Zones.All(z =>
                     (z.FromLeft?.Count ?? 0) == 0 && (z.FromRight?.Count ?? 0) == 0))
        {
            // A zone that names no category places nothing, so a ruleset made
            // only of empty zones would draw an empty panel with no complaint.
            problems["zones"] = ["A packing zone must name at least one device category."];
        }

        // The same referential check the shipped-seed test makes, enforced at
        // write time so a ruleset that only fails on site can never be saved.
        var active = await db.DeviceTypes
            .Where(d => d.Active)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (request.Payload.PreferredDevice is null || request.Payload.Terminals is null)
        {
            problems["payload"] = ["The ruleset payload is missing its preferred devices or terminal rules."];
            return problems;
        }

        var missing = Referenced(request.Payload).Distinct().Where(id => !active.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            problems["payload"] =
                [$"These device types are not in the catalogue, or are inactive: {string.Join(", ", missing)}."];
        }

        return problems;
    }

    private static async Task ClearDefaults(HouseConfigDbContext db, CancellationToken ct)
    {
        foreach (var existing in await db.RuleSets.Where(r => r.IsDefault).ToListAsync(ct))
        {
            existing.IsDefault = false;
        }
    }

    private static void Apply(DeviceTypeRow row, SaveDeviceTypeRequest request)
    {
        row.Manufacturer = request.Manufacturer.Trim();
        row.Model = request.Model.Trim();
        row.PartNumber = request.PartNumber.Trim();
        row.Category = request.Category;
        row.ModuleWidth = request.ModuleWidth;
        row.ChannelCount = request.ChannelCount;
        row.MaxLoadPerChannelW = request.MaxLoadPerChannelW;
        row.MaxTotalLoadW = request.MaxTotalLoadW;
        row.Cost = request.Cost;
        row.Active = request.Active;
    }

    private static void Apply(EnclosureTypeRow row, SaveEnclosureRequest request)
    {
        row.Manufacturer = request.Manufacturer.Trim();
        row.Model = request.Model.Trim();
        row.Rows = request.Rows;
        row.SlotsPerRow = request.SlotsPerRow;
        row.IpRating = request.IpRating;
        row.Cost = request.Cost;
    }
}
