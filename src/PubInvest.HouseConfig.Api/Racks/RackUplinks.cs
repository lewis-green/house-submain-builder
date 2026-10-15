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
        int? UplinkToPort,
        string? UplinkToText);

    public static IQueryable<Placed> Where(HouseConfigDbContext db, IQueryable<RackItem> items) =>
        from i in items
        join r in db.Racks on i.RackId equals r.Id
        from l in db.Locations.Where(l => l.Id == r.LocationId).DefaultIfEmpty()
        select new Placed(
            i.Id, i.Label, i.RackDeviceTypeId, r.Id, r.ProjectId, r.Name,
            r.LocationId, l == null ? null : l.Name, l == null ? null : l.Sequence,
            i.TopU, i.UplinkPort, i.UplinkToItemId, i.UplinkToPort, i.UplinkToText);

    /// The port a switch's own uplink leaves by, which no other uplink may
    /// arrive at — but only while that uplink still goes somewhere. One left
    /// pointing at nothing by the database's set-null holds no port. The
    /// picker is told this same port, so it and the check agree.
    public static int? HeldUplinkPort(Placed item) =>
        item.UplinkToItemId is not null || item.UplinkToText is not null ? item.UplinkPort : null;

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
        if (HeldUplinkPort(target) == port) return [("toPort", $"Port {port} is the uplink of {name}.")];

        if (await TakerAsync(db, target.Id, port, item.Id, ct) is { } taker)
        {
            return [("toPort", $"Port {port} on {name} already takes the uplink from {Name(taker, item.RackId)}.")];
        }

        if (await LoopAsync(db, item, target, ct) is { } through)
        {
            var via = through.Count == 0 ? "" : $" (through {Listed(through.Select(t => Name(t, item.RackId)).ToList())})";
            return [("toItemId", $"{name} already uplinks{via} into {item.Label}; that would make a loop.")];
        }

        return [];
    }

    /// The switches between the target and this item when the target's own
    /// uplinks lead back here — so this uplink would close a ring, which a
    /// switched network cannot have — or null when they lead elsewhere. The
    /// walk is bounded by the number of links, so links already in a ring
    /// (made before this check existed) cannot keep it going.
    private static async Task<List<Placed>?> LoopAsync(HouseConfigDbContext db, Placed item, Placed target, CancellationToken ct)
    {
        var links = (await Where(db, db.RackItems.Where(i =>
                i.UplinkToItemId != null && i.Id != item.Id && db.Racks.Any(r => r.Id == i.RackId && r.ProjectId == item.ProjectId)))
            .ToListAsync(ct))
            .ToDictionary(l => l.Id);

        var through = new List<Placed>();
        var current = target;
        for (var step = 0; step <= links.Count; step++)
        {
            if (!links.TryGetValue(current.Id, out var link) || link.UplinkToItemId is not { } next) return null;
            if (next == item.Id) return through;
            if (!links.TryGetValue(next, out var onward))
            {
                // The next switch has no uplink of its own: the chain ends there.
                return null;
            }

            through.Add(onward);
            current = onward;
        }

        return null;
    }

    /// "A", "A and B", "A, B and C".
    private static string Listed(List<string> names) =>
        names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} and {names[^1]}";

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
