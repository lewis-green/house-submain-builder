using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// A switch's uplink: what may be recorded, and how each end of one reads.
/// An uplink can reach into any rack of the house, so both ends are looked up
/// by id rather than found among the rack's own items.
public static class RackUplinks
{
    /// Enough for "ISP fibre in the meter cupboard", and short enough to print
    /// in the schedule's label column.
    public const int MaxText = 80;

    /// An item with the rack and location it is in, for saying where it is.
    public sealed record Placed(
        Guid Id,
        string Label,
        Guid RackDeviceTypeId,
        Guid RackId,
        Guid ProjectId,
        string RackName,
        Guid? LocationId,
        string? LocationName,
        int? LocationSequence,
        int TopU,
        int? UplinkPort,
        Guid? UplinkToItemId,
        int? UplinkToPort);

    public static IQueryable<Placed> Where(HouseConfigDbContext db, IQueryable<RackItem> items) =>
        from i in items
        join r in db.Racks on i.RackId equals r.Id
        from l in db.Locations.Where(l => l.Id == r.LocationId).DefaultIfEmpty()
        select new Placed(
            i.Id, i.Label, i.RackDeviceTypeId, r.Id, r.ProjectId, r.Name,
            r.LocationId, l == null ? null : l.Name, l == null ? null : l.Sequence,
            i.TopU, i.UplinkPort, i.UplinkToItemId, i.UplinkToPort);

    /// "Switch 1" beside the switch being edited; "Switch 1 in Garage" from
    /// another rack, where its own rack's "Switch 1" would be a different one.
    public static string Name(Placed item, Guid fromRackId) =>
        item.RackId == fromRackId ? item.Label : $"{item.Label} in {item.RackName}";

    /// Both ends of every link the items have: where each one's uplink goes,
    /// and whose uplinks arrive at each.
    public sealed class Links(Dictionary<Guid, Placed> targets, List<Placed> sources)
    {
        public RackUplinkResponse? UplinkOf(RackItem item)
        {
            if (item.UplinkPort is not { } port) return null;
            if (item.UplinkToText is { } text) return new RackUplinkResponse(port, null, null, text, null, null, null, null);

            // A destination missing here was removed without its uplinks being
            // cleared (the database's set-null), which reads as no uplink at all.
            if (item.UplinkToItemId is not { } to || item.UplinkToPort is not { } toPort || !targets.TryGetValue(to, out var target))
            {
                return null;
            }

            return new RackUplinkResponse(port, to, toPort, null, target.Label, target.RackId, target.RackName, target.LocationName);
        }

        public IReadOnlyList<RackDownlinkResponse> DownlinksInto(Guid itemId) =>
            sources
                .Where(s => s.UplinkToItemId == itemId && s.UplinkToPort is not null)
                .OrderBy(s => s.UplinkToPort)
                .Select(s => new RackDownlinkResponse(s.UplinkToPort!.Value, s.Id, s.Label, s.RackId, s.RackName))
                .ToList();
    }

    public static async Task<Links> LinksAsync(HouseConfigDbContext db, IReadOnlyCollection<RackItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var targetIds = items.Where(i => i.UplinkToItemId is not null).Select(i => i.UplinkToItemId!.Value).Distinct().ToList();

        var targets = targetIds.Count == 0
            ? []
            : (await Where(db, db.RackItems.Where(i => targetIds.Contains(i.Id))).ToListAsync(ct)).ToDictionary(t => t.Id);
        var sources = await Where(db, db.RackItems.Where(i => i.UplinkToItemId != null && ids.Contains(i.UplinkToItemId.Value)))
            .ToListAsync(ct);

        return new Links(targets, sources);
    }

    /// The problems with recording this uplink on this item, keyed by the
    /// request field each belongs to, and the words trimmed when that is the form.
    public static async Task<(Dictionary<string, string[]> Problems, string? Text)> ValidateAsync(
        HouseConfigDbContext db,
        Placed item,
        RackDeviceType type,
        SaveRackUplinkRequest request,
        CancellationToken ct)
    {
        var problems = new Dictionary<string, string[]>();
        var text = string.IsNullOrWhiteSpace(request.ToText) ? null : request.ToText.Trim();

        if (type.Kind != RackDeviceKind.Switch)
        {
            problems["item"] = [$"{item.Label} is not a switch. Only a switch has an uplink."];
            return (problems, text);
        }

        if (type.GroupOf(request.Port) is null)
        {
            problems["port"] = [$"{item.Label} has no port {request.Port}; its ports are {Range(type)}."];
        }
        else if (await TakerAsync(db, item.Id, request.Port, null, ct) is { } arriving)
        {
            problems["port"] = [$"Port {request.Port} takes the uplink from {Name(arriving, item.RackId)}. Choose another."];
        }

        if (text is not null && request.ToItemId is not null)
        {
            problems["toText"] = ["Choose another switch or say where it goes in words, not both."];
        }
        else if (text is null && request.ToItemId is null)
        {
            problems["toItemId"] = ["Say where the uplink goes: another switch's port, or in words."];
        }
        else if (text is not null)
        {
            if (request.ToPort is not null) problems["toPort"] = ["A port is only chosen on another switch."];
            if (text.Length > MaxText) problems["toText"] = [$"Keep it to {MaxText} characters; this is {text.Length}."];
        }
        else
        {
            foreach (var (field, message) in await DestinationProblemsAsync(db, item, request.ToItemId!.Value, request.ToPort, ct))
            {
                problems[field] = [message];
            }
        }

        return (problems, text);
    }

    private static async Task<IEnumerable<(string Field, string Message)>> DestinationProblemsAsync(
        HouseConfigDbContext db,
        Placed item,
        Guid toItemId,
        int? toPort,
        CancellationToken ct)
    {
        if (toItemId == item.Id) return [("toItemId", "A switch cannot uplink into itself.")];

        var target = await Where(db, db.RackItems.Where(i => i.Id == toItemId)).SingleOrDefaultAsync(ct);
        if (target is null || target.ProjectId != item.ProjectId) return [("toItemId", "Choose a switch in this house.")];

        var name = Name(target, item.RackId);
        var type = DomainMapper.ToDomain(await db.RackDeviceTypes.SingleAsync(t => t.Id == target.RackDeviceTypeId, ct));
        if (type.Kind != RackDeviceKind.Switch)
        {
            return [("toItemId", $"{name} is not a switch. An uplink goes into another switch, or say where it goes in words.")];
        }

        if (toPort is not { } port) return [("toPort", $"Choose the port on {name} it plugs into.")];
        if (type.GroupOf(port) is null) return [("toPort", $"{name} has no port {port}; its ports are {Range(type)}.")];
        if (target.UplinkPort == port) return [("toPort", $"Port {port} is the uplink of {name}.")];

        if (await TakerAsync(db, target.Id, port, item.Id, ct) is { } taker)
        {
            return [("toPort", $"Port {port} on {name} already takes the uplink from {Name(taker, item.RackId)}.")];
        }

        return [];
    }

    /// The switch, other than `except`, whose uplink arrives at this port.
    private static Task<Placed?> TakerAsync(HouseConfigDbContext db, Guid itemId, int port, Guid? except, CancellationToken ct) =>
        Where(db, db.RackItems.Where(i => i.UplinkToItemId == itemId && i.UplinkToPort == port && i.Id != except))
            .FirstOrDefaultAsync(ct);

    private static string Range(RackDeviceType type) =>
        $"{type.PortGroups.Min(g => g.From)}–{type.PortGroups.Max(g => g.To)}";

    /// The uplink as an issued drawing prints it.
    public static RackUplinkSnapshot? Printed(RackUplinkResponse? uplink, Guid rackId) => uplink switch
    {
        null => null,
        { ToText: { } text } => new RackUplinkSnapshot(uplink.Port, text, null, null, null, null),
        _ when uplink.ToRackId == rackId => new RackUplinkSnapshot(uplink.Port, null, uplink.ToLabel, null, null, uplink.ToPort),
        _ => new RackUplinkSnapshot(uplink.Port, null, uplink.ToLabel, uplink.ToRackName, uplink.ToLocationName, uplink.ToPort),
    };

    public static IReadOnlyList<RackDownlinkSnapshot>? Printed(IReadOnlyList<RackDownlinkResponse> downlinks, Guid rackId) =>
        downlinks.Count == 0
            ? null
            : downlinks.Select(d => new RackDownlinkSnapshot(d.Port, d.FromLabel, d.FromRackId == rackId ? null : d.FromRackName)).ToList();

    public static void Clear(RackItem item)
    {
        item.UplinkPort = null;
        item.UplinkToItemId = null;
        item.UplinkToPort = null;
        item.UplinkToText = null;
    }

    /// Tracks and clears every uplink into this item, for the caller's save.
    public static async Task ClearIntoAsync(HouseConfigDbContext db, Guid itemId, CancellationToken ct)
    {
        foreach (var source in await db.RackItems.Where(i => i.UplinkToItemId == itemId).ToListAsync(ct)) Clear(source);
    }
}
