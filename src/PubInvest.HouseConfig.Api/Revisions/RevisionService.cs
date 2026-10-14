using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Generation;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Api.Revisions;

public sealed class RevisionService(HouseConfigDbContext db)
{
    /// Snapshots the stored layout together with the rules and catalogue entries
    /// it used. Returns an error string for anything the user can fix.
    public async Task<(PanelRevision? Revision, string? Error)> IssueAsync(
        Guid submainId,
        string issuedBy,
        CancellationToken ct)
    {
        var (snapshot, submain, error) = await BuildAsync(submainId, ct);
        if (snapshot is null || submain is null) return (null, error);

        var revision = new PanelRevision
        {
            Id = Guid.NewGuid(),
            SubmainId = submainId,
            LayoutVersion = submain.LayoutVersion,
            SnapshotJson = JsonSerializer.Serialize(snapshot, DomainMapper.Json),
            IssuedAt = DateTimeOffset.UtcNow,
            IssuedBy = issuedBy,
        };

        db.PanelRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        return (revision, null);
    }

    /// Whether a revision still says what the submain says now: its embedded
    /// snapshot is compared, byte for byte, with the one issuing this moment
    /// would embed. That covers everything the drawing and schedule print — a
    /// moved device, a renamed circuit or room, a changed fixture or install
    /// option, a renamed submain — without the client having to know which
    /// edits matter. LayoutVersion alone cannot: a rename changes no geometry,
    /// so it never bumped the version, and the PDF went quietly out of date.
    /// A submain that can no longer be issued (no panel, say) is current to
    /// no revision.
    public async Task<Func<PanelRevision, bool>> CurrentCheckAsync(Guid submainId, CancellationToken ct)
    {
        var (snapshot, _, _) = await BuildAsync(submainId, ct);
        var now = snapshot is null ? null : JsonSerializer.Serialize(snapshot, DomainMapper.Json);
        // The stored JSON goes once through the model before the comparison, so a
        // revision issued before a field joined the snapshot is judged on what it
        // says, not on the shape of its text.
        return revision => now is not null && JsonSerializer.Serialize(Read(revision), DomainMapper.Json) == now;
    }

    /// Where a revision stands in its submain's history, counted from one in the
    /// order they were issued: the "revision 3" a drawing is referred to by. The
    /// same order as ListAsync, so the number a revision is issued with is the
    /// number the list gives it — including when two were issued in the same
    /// tick, where the clock alone could not separate them.
    public async Task<int> NumberOfAsync(PanelRevision revision, CancellationToken ct)
        => (await Ordered(revision.SubmainId).Select(r => r.Id).ToListAsync(ct)).IndexOf(revision.Id) + 1;

    /// Oldest first. A timestamp can tie, so the id breaks the tie, and every
    /// place that orders revisions comes here to agree on it.
    private IOrderedQueryable<PanelRevision> Ordered(Guid submainId)
        => db.PanelRevisions
            .Where(r => r.SubmainId == submainId)
            .OrderBy(r => r.IssuedAt)
            .ThenBy(r => r.Id);

    private async Task<(RevisionSnapshot? Snapshot, Submain? Submain, string? Error)> BuildAsync(
        Guid submainId,
        CancellationToken ct)
    {
        var submain = await db.Submains
            .Include(s => s.Circuits)
            .Include(s => s.Project)
            .SingleOrDefaultAsync(s => s.Id == submainId, ct);

        if (submain is null) return (null, null, null);

        var devices = await db.DeviceInstances
            .Include(d => d.Channels)
            .Where(d => d.SubmainId == submainId)
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            return (null, null, "This automation panel has no layout yet. Generate one before issuing a revision.");
        }

        if (submain.EnclosureTypeId is null)
        {
            return (null, null, "This automation panel has no enclosure.");
        }

        var enclosureRow = await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);
        if (enclosureRow is null) return (null, null, "This automation panel's enclosure is not in the catalogue.");

        var ruleSetRow = submain.RuleSetId is null
            ? await db.RuleSets.FirstOrDefaultAsync(r => r.IsDefault, ct)
            : await db.RuleSets.SingleOrDefaultAsync(r => r.Id == submain.RuleSetId, ct);

        if (ruleSetRow is null) return (null, null, "No ruleset is available.");

        // Only the device types actually placed: a revision records what this
        // panel is made of, not the whole catalogue as it happened to look.
        var placedTypeIds = devices.Select(d => d.DeviceTypeId).Distinct().ToList();
        var catalogue = (await db.DeviceTypes
                .Where(t => placedTypeIds.Contains(t.Id))
                .OrderBy(t => t.PartNumber)
                .ToListAsync(ct))
            .Select(DomainMapper.ToDomain)
            .ToList();

        var layout = new PanelLayout(
            enclosureRow.Rows,
            enclosureRow.SlotsPerRow,
            devices.Select(d => new PlacedDevice(
                d.DeviceTypeId,
                Enum.Parse<Domain.Catalogue.DeviceCategory>(d.Category),
                d.RowIndex,
                d.StartSlot,
                d.ModuleWidth,
                d.Label,
                d.Channels.OrderBy(c => c.ChannelIndex)
                    .Select(c => new ChannelAssignment(c.ChannelIndex, c.CircuitId, c.IsSpare))
                    .ToList(),
                Enum.Parse<TerminalRole>(d.TerminalRole),
                d.CircuitId)).ToList());

        // Accessories are not placed devices, so they have to be recomputed from
        // the same rules and circuits the panel was generated from.
        var fullCatalogue = new Domain.Catalogue.DeviceCatalogue(
            (await db.DeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain));

        var rules = DomainMapper.ToDomain(ruleSetRow);
        var domainCircuits = submain.Circuits.Select(DomainMapper.ToDomain).ToList();
        // Everything costed but not placed: jumper bars and end stops, and the
        // external LED driver. Missing the driver here would ship a bill of
        // materials you could order from and still have no way to light the tape.
        var accessories = TerminalBandBuilder.Build(domainCircuits, rules, fullCatalogue).Accessories
            .Concat(TapeSupplySizer.Size(domainCircuits, rules, fullCatalogue).ExternalParts)
            .ToList();

        var bom = BomBuilder.Build(layout, accessories, DomainMapper.ToDomain(enclosureRow), fullCatalogue);

        var accessoryTypes = accessories
            .Select(a => fullCatalogue.Find(a.DeviceTypeId))
            .OfType<Domain.Catalogue.DeviceType>();

        var snapshot = new RevisionSnapshot(
            submain.Project?.Name ?? "",
            submain.Name,
            submain.Reference,
            layout,
            submain.Circuits
                .OrderBy(c => c.Sequence)
                .Select(c => new CircuitSnapshot(c.Id, c.Type, c.Name, c.Room, c.Sequence, c.TerminalPosition, c.TerminalCount))
                .ToList(),
            rules,
            catalogue.Concat(accessoryTypes).DistinctBy(t => t.Id).OrderBy(t => t.PartNumber).ToList(),
            DomainMapper.ToDomain(enclosureRow),
            bom,
            submain.HasIsolator,
            submain.TerminalsAtBottom);

        return (snapshot, submain, null);
    }

    public async Task<PanelRevision?> FindAsync(Guid revisionId, CancellationToken ct)
        => await db.PanelRevisions.SingleOrDefaultAsync(r => r.Id == revisionId, ct);

    public async Task<PanelRevision?> LatestAsync(Guid submainId, CancellationToken ct)
        => await Ordered(submainId).LastOrDefaultAsync(ct);

    /// Newest first.
    public async Task<List<PanelRevision>> ListAsync(Guid submainId, CancellationToken ct)
        => await Ordered(submainId).Reverse().ToListAsync(ct);

    public static RevisionSnapshot Read(PanelRevision revision)
        => JsonSerializer.Deserialize<RevisionSnapshot>(revision.SnapshotJson, DomainMapper.Json)
           ?? throw new InvalidOperationException($"Revision {revision.Id} has an unreadable snapshot.");
}
