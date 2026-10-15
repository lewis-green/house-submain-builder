using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Locations;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Builds what the endpoints return, from the stored rack. The layout is the
/// one place the UI learns where anything is.
public static class RackViews
{
    public static async Task<Dictionary<Guid, RackDeviceType>> TypesAsync(
        HouseConfigDbContext db,
        IEnumerable<RackItem> items,
        CancellationToken ct)
    {
        var ids = items.Select(i => i.RackDeviceTypeId).Distinct().ToList();
        return (await db.RackDeviceTypes.Where(t => ids.Contains(t.Id)).ToListAsync(ct))
            .Select(DomainMapper.ToDomain)
            .ToDictionary(t => t.Id);
    }

    /// Needs rack.Items loaded.
    public static async Task<List<RackSpan>> SpansAsync(HouseConfigDbContext db, Rack rack, CancellationToken ct)
    {
        var types = await TypesAsync(db, rack.Items, ct);
        return rack.Items.Select(i => new RackSpan(i.Id, i.TopU, types[i.RackDeviceTypeId].HeightU)).ToList();
    }

    /// Needs rack.Items loaded.
    public static async Task<RackResponse> ResponseAsync(HouseConfigDbContext db, Rack rack, CancellationToken ct)
    {
        var cabinet = DomainMapper.ToDomain(await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct));
        var spans = await SpansAsync(db, rack, ct);
        var feed = rack.FedFromSubmainId is { } submainId
            ? await db.Submains.Where(s => s.Id == submainId).Select(s => s.Name).SingleOrDefaultAsync(ct)
            : null;

        return new RackResponse(
            rack.Id, rack.ProjectId, rack.Name, cabinet.Id, cabinet.Description, cabinet.HeightU,
            spans.Sum(s => s.HeightU), rack.FedFromSubmainId, feed, rack.Notes, rack.LayoutVersion,
            rack.LocationId);
    }

    public static async Task<RackLayoutResponse?> LayoutAsync(HouseConfigDbContext db, Guid rackId, CancellationToken ct)
    {
        var rack = await db.Racks
            .AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Ports)
            .SingleOrDefaultAsync(r => r.Id == rackId, ct);
        if (rack is null) return null;

        var cabinet = await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct);
        var types = await TypesAsync(db, rack.Items, ct);
        var spans = rack.Items.Select(i => new RackSpan(i.Id, i.TopU, types[i.RackDeviceTypeId].HeightU)).ToList();
        var links = await RackUplinks.LinksAsync(db, rack.Items, ct);

        return new RackLayoutResponse(
            rack.Id,
            cabinet.HeightU,
            RackPlacement.FreeU(cabinet.HeightU, spans),
            rack.LayoutVersion,
            rack.Items
                .OrderByDescending(i => i.TopU)
                .Select(i => Item(i, types[i.RackDeviceTypeId], links))
                .ToList());
    }

    public static RackItemResponse Item(RackItem item, RackDeviceType type, RackUplinks.Links links) => new(
        item.Id, type.Id, type.Kind, type.Description, type.PartNumber, item.Label, item.TopU, type.HeightU,
        type.PortGroups, type.PoeBudgetW,
        item.Ports.OrderBy(p => p.Number).Select(p => new RackPortResponse(p.Number, p.Label, p.Poe)).ToList(),
        links.UplinkOf(item),
        links.DownlinksInto(item.Id));

    public static async Task<Dictionary<string, string[]>> ValidateAsync(
        HouseConfigDbContext db,
        Guid projectId,
        SaveRackRequest request,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems["name"] = ["A rack name is required."];
        }

        if (!await db.RackTypes.AnyAsync(t => t.Id == request.RackTypeId, ct))
        {
            problems["rackTypeId"] = ["Choose a cabinet from the catalogue."];
        }

        if (request.FedFromSubmainId is { } feed
            && !await db.Submains.AnyAsync(s => s.Id == feed && s.ProjectId == projectId, ct))
        {
            problems["fedFromSubmainId"] = ["Choose an automation panel in this house, or none."];
        }

        if (await LocationRules.ProblemAsync(db, projectId, request.LocationId, ct) is { } misplaced)
        {
            problems["locationId"] = [misplaced];
        }

        return problems;
    }
}
