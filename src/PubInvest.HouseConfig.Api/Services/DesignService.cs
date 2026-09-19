using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Endpoints;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Services;

public sealed record DesignInputs(
    Data.Entities.Submain Submain,
    GenerationRequest Request,
    IReadOnlyList<Circuit> Circuits);

public sealed class DesignService(HouseConfigDbContext db)
{
    /// Loads everything the generator needs. Returns an error string for anything
    /// the user can fix (no enclosure, no ruleset), never an exception.
    public async Task<(DesignInputs? Inputs, string? Error)> LoadAsync(
        Guid submainId,
        PreviewRequest? overrides,
        CancellationToken ct)
    {
        var submain = await db.Submains
            .Include(s => s.Circuits)
            .SingleOrDefaultAsync(s => s.Id == submainId, ct);

        if (submain is null) return (null, null);

        var enclosureId = overrides?.EnclosureTypeId ?? submain.EnclosureTypeId;
        if (enclosureId is null)
        {
            return (null, "This submain has no enclosure. Choose one before generating a design.");
        }

        var enclosureRow = await db.Enclosures.SingleOrDefaultAsync(e => e.Id == enclosureId, ct);
        if (enclosureRow is null)
        {
            return (null, $"Enclosure {enclosureId} is not in the catalogue.");
        }

        var ruleSetId = overrides?.RuleSetId ?? submain.RuleSetId;
        var ruleSetRow = ruleSetId is null
            ? await db.RuleSets.FirstOrDefaultAsync(r => r.IsDefault, ct)
            : await db.RuleSets.SingleOrDefaultAsync(r => r.Id == ruleSetId, ct);

        if (ruleSetRow is null)
        {
            return (null, "No ruleset is available. Seed or select one before generating a design.");
        }

        var circuits = overrides?.Circuits is { Count: > 0 }
            ? overrides.Circuits.Select(c => new Circuit(
                c.Id ?? Guid.NewGuid(), Enum.Parse<CircuitType>(c.Type), c.Name, c.Room, c.Sequence,
                c.WattsPerMetre, c.LengthMetres)).ToList()
            : submain.Circuits.Select(DomainMapper.ToDomain).ToList();

        var catalogue = new DeviceCatalogue(
            (await db.DeviceTypes.ToListAsync(ct)).Select(DomainMapper.ToDomain));

        var allEnclosures = (await db.Enclosures.ToListAsync(ct))
            .Select(DomainMapper.ToDomain).ToList();

        // Positions the engineer chose by dragging. Keyed by label, so they
        // survive the device rows being replaced.
        var positionOverrides = (await db.PositionOverrides
                .Where(o => o.SubmainId == submainId)
                .OrderBy(o => o.Label)
                .ToListAsync(ct))
            .Select(o => new PositionOverride(o.Label, o.RowIndex, o.StartSlot))
            .ToList();

        var request = new GenerationRequest(
            circuits,
            DomainMapper.ToDomain(enclosureRow),
            DomainMapper.ToDomain(ruleSetRow),
            catalogue,
            allEnclosures,
            positionOverrides);

        return (new DesignInputs(submain, request, circuits), null);
    }

    public async Task<(DesignResponse? Response, string? Error, bool HasErrors)> GenerateAsync(
        Guid submainId,
        PreviewRequest? request,
        CancellationToken ct)
    {
        var (inputs, error) = await LoadAsync(submainId, request, ct);
        if (error is not null) return (null, error, false);
        if (inputs is null) return (null, null, false);

        var result = PanelGenerator.Generate(inputs.Request);

        if (result.HasErrors)
        {
            return (ToResponse(submainId, inputs.Submain.LayoutVersion, result, inputs.Circuits), null, true);
        }

        var submain = inputs.Submain;

        // A circuit list in the request body becomes the stored one, so the
        // wizard's "generate" is a single call.
        if (request?.Circuits is { Count: > 0 })
        {
            SubmainEndpoints.ApplyCircuits(db, submain, inputs.Circuits.Select(c => new Data.Entities.CircuitRow
            {
                Id = c.Id,
                SubmainId = submain.Id,
                Type = c.Type.ToString(),
                Name = c.Name,
                Room = c.Room,
                Sequence = c.Sequence,
                WattsPerMetre = c.WattsPerMetre,
                LengthMetres = c.LengthMetres
            }).ToList());
        }

        var existingDevices = await db.DeviceInstances
            .Include(d => d.Channels)
            .Where(d => d.SubmainId == submainId)
            .ToListAsync(ct);

        var orphans = OrphanReporter.Report(
            result.Layout,
            existingDevices.Select(DomainMapper.ToExisting).ToList());

        db.DeviceInstances.RemoveRange(existingDevices);

        // The save between the delete and the insert is what keeps the unique
        // (SubmainId, RowIndex, StartSlot) index from tripping on a rearrangement.
        // Do not collapse the two saves into one.
        await db.SaveChangesAsync(ct);

        var persisted = new Dictionary<string, Guid>();

        foreach (var placed in result.Layout.Devices)
        {
            var row = new Data.Entities.DeviceInstance
            {
                Id = Guid.NewGuid(),
                SubmainId = submainId,
                DeviceTypeId = placed.DeviceTypeId,
                Category = placed.Category.ToString(),
                RowIndex = placed.RowIndex,
                StartSlot = placed.StartSlot,
                ModuleWidth = placed.ModuleWidth,
                Label = placed.Label,
                TerminalRole = placed.TerminalRole.ToString(),
                Channels = placed.Channels.Select(c => new Data.Entities.DeviceChannelRow
                {
                    Id = Guid.NewGuid(),
                    ChannelIndex = c.ChannelIndex,
                    CircuitId = c.CircuitId,
                    IsSpare = c.IsSpare
                }).ToList()
            };

            db.DeviceInstances.Add(row);
            persisted[row.Label] = row.Id;
        }

        submain.LayoutVersion++;
        await db.SaveChangesAsync(ct);

        var diagnostics = result.Diagnostics.Concat(orphans).ToList();
        var mergedResult = new GenerationResult(result.Layout, diagnostics, result.Bom);

        return (ToResponse(submainId, submain.LayoutVersion, mergedResult, inputs.Circuits, persisted), null, false);
    }

    /// `persisted` is null for a preview (nothing is stored yet) and populated by
    /// generate, so a saved design's device ids reach the client through the same shape.
    public static DesignResponse ToResponse(
        Guid submainId,
        int layoutVersion,
        GenerationResult result,
        IReadOnlyList<Circuit> circuits,
        IReadOnlyDictionary<string, Guid>? persisted = null)
    {
        var byId = circuits.ToDictionary(c => c.Id);

        var devices = result.Layout.Devices
            .OrderBy(d => d.RowIndex).ThenBy(d => d.StartSlot)
            .Select(d =>
            {
                Guid? storedId =
                    persisted is not null && persisted.TryGetValue(d.Label, out var found) ? found : null;

                return new PlacedDeviceResponse(
                    storedId,
                    d.DeviceTypeId,
                    d.Category.ToString(),
                    d.RowIndex,
                    d.StartSlot,
                    d.ModuleWidth,
                    d.Label,
                    d.TerminalRole.ToString(),
                    d.Channels.Select(c =>
                    {
                        var circuit = c.CircuitId is not null && byId.TryGetValue(c.CircuitId.Value, out var match)
                            ? match
                            : null;

                        return new ChannelResponse(
                            c.ChannelIndex, c.CircuitId, circuit?.Name, circuit?.Room, c.IsSpare);
                    }).ToList());
            })
            .ToList();

        var rowsUsed = result.Layout.Devices.Count == 0 ? 0 : result.Layout.Devices.Max(d => d.RowIndex) + 1;

        return new DesignResponse(
            submainId,
            layoutVersion,
            new LayoutResponse(result.Layout.Rows, result.Layout.SlotsPerRow, devices),
            result.Diagnostics.Select(d => new DiagnosticResponse(
                d.Severity.ToString(), d.Code, d.Message, d.Suggestion)).ToList(),
            result.Bom.Lines.Select(l => new BomLineResponse(
                l.CatalogueId, l.PartNumber, l.Description, l.Quantity, l.PanelMounted)).ToList(),
            new DesignSummary(
                rowsUsed,
                result.Layout.SlotsUsed,
                result.Layout.TotalSlots,
                result.Layout.Devices.Count,
                result.Layout.Devices.SelectMany(d => d.Channels).Count(c => c.IsSpare)));
    }
}
