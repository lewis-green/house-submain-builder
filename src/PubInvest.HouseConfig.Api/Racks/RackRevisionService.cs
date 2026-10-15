using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Racks;

namespace PubInvest.HouseConfig.Api.Racks;

/// Issues rack revisions and judges whether one is still current, the same way
/// RevisionService does for panels.
public sealed class RackRevisionService(HouseConfigDbContext db)
{
    public async Task<(RackRevision? Revision, string? Error)> IssueAsync(
        Guid rackId,
        string issuedBy,
        CancellationToken ct)
    {
        var (snapshot, rack, error) = await BuildAsync(rackId, ct);
        if (snapshot is null || rack is null) return (null, error);

        var revision = new RackRevision
        {
            Id = Guid.NewGuid(),
            RackId = rackId,
            LayoutVersion = rack.LayoutVersion,
            SnapshotJson = JsonSerializer.Serialize(snapshot, DomainMapper.Json),
            IssuedAt = DateTimeOffset.UtcNow,
            IssuedBy = issuedBy,
        };

        db.RackRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        return (revision, null);
    }

    /// Current when the revision's snapshot says exactly what issuing now would:
    /// a label, a tick, a move, a cabinet or a feed change all show, without the
    /// client knowing which edits matter. A rack that can no longer be issued
    /// (emptied, say) is current to no revision.
    public async Task<Func<RackRevision, bool>> CurrentCheckAsync(Guid rackId, CancellationToken ct)
    {
        var (snapshot, _, _) = await BuildAsync(rackId, ct);
        var now = snapshot is null ? null : Printed(snapshot);
        return revision => now is not null && Printed(Read(revision)) == now;
    }

    /// The snapshot as the PDF prints it. Whether a catalogue row is still
    /// offered is embedded with it but printed nowhere, so retiring a switch
    /// must not make every issued rack that holds one out of date. The stored
    /// snapshot keeps the row as it was; only the comparison ignores it.
    private static string Printed(RackSnapshot snapshot) =>
        JsonSerializer.Serialize(
            snapshot with { Catalogue = snapshot.Catalogue.Select(t => t with { Active = true }).ToList() },
            DomainMapper.Json);

    /// Counted from one in issue order — the "revision 3" a drawing is called by.
    public async Task<int> NumberOfAsync(RackRevision revision, CancellationToken ct)
        => (await Ordered(revision.RackId).Select(r => r.Id).ToListAsync(ct)).IndexOf(revision.Id) + 1;

    public async Task<RackRevision?> FindAsync(Guid revisionId, CancellationToken ct)
        => await db.RackRevisions.SingleOrDefaultAsync(r => r.Id == revisionId, ct);

    public async Task<RackRevision?> LatestAsync(Guid rackId, CancellationToken ct)
        => await Ordered(rackId).LastOrDefaultAsync(ct);

    /// Newest first.
    public async Task<List<RackRevision>> ListAsync(Guid rackId, CancellationToken ct)
        => await Ordered(rackId).Reverse().ToListAsync(ct);

    public static RackSnapshot Read(RackRevision revision)
        => JsonSerializer.Deserialize<RackSnapshot>(revision.SnapshotJson, DomainMapper.Json)
           ?? throw new InvalidOperationException($"Rack revision {revision.Id} has an unreadable snapshot.");

    /// Oldest first; the id breaks a tie between two issued in the same tick.
    private IOrderedQueryable<RackRevision> Ordered(Guid rackId)
        => db.RackRevisions.Where(r => r.RackId == rackId).OrderBy(r => r.IssuedAt).ThenBy(r => r.Id);

    private async Task<(RackSnapshot? Snapshot, Rack? Rack, string? Error)> BuildAsync(Guid rackId, CancellationToken ct)
    {
        var rack = await db.Racks
            .AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Ports)
            .SingleOrDefaultAsync(r => r.Id == rackId, ct);

        if (rack is null) return (null, null, null);
        if (rack.Items.Count == 0)
        {
            return (null, null, "This rack is empty. Add something to it before issuing a revision.");
        }

        var project = await db.Projects.SingleAsync(p => p.Id == rack.ProjectId, ct);
        var cabinet = DomainMapper.ToDomain(await db.RackTypes.SingleAsync(t => t.Id == rack.RackTypeId, ct));
        var feed = rack.FedFromSubmainId is { } submainId
            ? await db.Submains.Where(s => s.Id == submainId).Select(s => s.Name).SingleOrDefaultAsync(ct)
            : null;

        // The whole rack catalogue, inactive rows included: an item retired
        // after it was placed is still in the rack and still has to be drawn,
        // scheduled and ordered.
        var catalogue = (await db.RackDeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain).ToDictionary(t => t.Id);
        var placed = rack.Items.Select(i => catalogue[i.RackDeviceTypeId]).ToList();

        var orphan = placed.FirstOrDefault(t => t.RequiredAccessoryId is { } a && !catalogue.ContainsKey(a));
        if (orphan is not null)
        {
            return (null, null, $"{orphan.Description} needs an accessory that is no longer in the catalogue.");
        }

        var used = placed
            .Concat(placed.Where(t => t.RequiredAccessoryId is not null).Select(t => catalogue[t.RequiredAccessoryId!.Value]))
            .DistinctBy(t => t.Id)
            .OrderBy(t => t.PartNumber, StringComparer.Ordinal)
            .ToList();

        // Both ends of each link are printed, so a switch in another rack
        // uplinking into this one, or that rack's rename, dates this PDF too.
        var links = await RackUplinks.LinksAsync(db, rack.Items, ct);

        var snapshot = new RackSnapshot(
            project.Name,
            rack.Name,
            cabinet,
            feed,
            rack.Items
                .OrderByDescending(i => i.TopU)
                .Select(i => new RackItemSnapshot(
                    i.RackDeviceTypeId,
                    i.Label,
                    i.TopU,
                    catalogue[i.RackDeviceTypeId].HeightU,
                    i.Ports.OrderBy(p => p.Number).Select(p => new RackPortSnapshot(p.Number, p.Label, p.Poe)).ToList(),
                    RackUplinks.Printed(links.UplinkOf(i), rack.Id),
                    RackUplinks.Printed(links.DownlinksInto(i.Id), rack.Id)))
                .ToList(),
            used,
            RackBom.Build(cabinet, placed, catalogue),
            rack.LocationId is { } locationId
                ? await db.Locations.Where(l => l.Id == locationId).Select(l => l.Name).SingleOrDefaultAsync(ct)
                : null);

        return (snapshot, rack, null);
    }
}
