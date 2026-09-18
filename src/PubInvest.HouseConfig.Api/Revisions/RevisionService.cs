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
        var submain = await db.Submains
            .Include(s => s.Circuits)
            .Include(s => s.Project)
            .SingleOrDefaultAsync(s => s.Id == submainId, ct);

        if (submain is null) return (null, null);

        var devices = await db.DeviceInstances
            .Include(d => d.Channels)
            .Where(d => d.SubmainId == submainId)
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            return (null, "This submain has no panel yet. Generate one before issuing a revision.");
        }

        if (submain.EnclosureTypeId is null)
        {
            return (null, "This submain has no enclosure.");
        }

        var enclosureRow = await db.Enclosures.SingleOrDefaultAsync(e => e.Id == submain.EnclosureTypeId, ct);
        if (enclosureRow is null) return (null, "This submain's enclosure is not in the catalogue.");

        var ruleSetRow = submain.RuleSetId is null
            ? await db.RuleSets.FirstOrDefaultAsync(r => r.IsDefault, ct)
            : await db.RuleSets.SingleOrDefaultAsync(r => r.Id == submain.RuleSetId, ct);

        if (ruleSetRow is null) return (null, "No ruleset is available.");

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
                Enum.Parse<TerminalRole>(d.TerminalRole))).ToList());

        // Accessories are not placed devices, so they have to be recomputed from
        // the same rules and circuits the panel was generated from.
        var fullCatalogue = new Domain.Catalogue.DeviceCatalogue(
            (await db.DeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain));

        var rules = DomainMapper.ToDomain(ruleSetRow);
        var domainCircuits = submain.Circuits.Select(DomainMapper.ToDomain).ToList();
        var accessories = TerminalBandBuilder.Build(domainCircuits, rules, fullCatalogue).Accessories;

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
                .Select(c => new CircuitSnapshot(c.Id, c.Type, c.Name, c.Room, c.Sequence))
                .ToList(),
            rules,
            catalogue.Concat(accessoryTypes).DistinctBy(t => t.Id).OrderBy(t => t.PartNumber).ToList(),
            DomainMapper.ToDomain(enclosureRow),
            bom);

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

    public async Task<PanelRevision?> FindAsync(Guid revisionId, CancellationToken ct)
        => await db.PanelRevisions.SingleOrDefaultAsync(r => r.Id == revisionId, ct);

    public async Task<PanelRevision?> LatestAsync(Guid submainId, CancellationToken ct)
        => await db.PanelRevisions
            .Where(r => r.SubmainId == submainId)
            .OrderByDescending(r => r.IssuedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<List<PanelRevision>> ListAsync(Guid submainId, CancellationToken ct)
        => await db.PanelRevisions
            .Where(r => r.SubmainId == submainId)
            .OrderByDescending(r => r.IssuedAt)
            .ToListAsync(ct);

    public static RevisionSnapshot Read(PanelRevision revision)
        => JsonSerializer.Deserialize<RevisionSnapshot>(revision.SnapshotJson, DomainMapper.Json)
           ?? throw new InvalidOperationException($"Revision {revision.Id} has an unreadable snapshot.");
}
