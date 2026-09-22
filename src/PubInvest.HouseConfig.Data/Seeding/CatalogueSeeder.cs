using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;

namespace PubInvest.HouseConfig.Data.Seeding;

public static class CatalogueSeeder
{
    /// Additive and idempotent for catalogue rows: one whose id already exists is
    /// left untouched, so an admin's edit is never overwritten by a redeploy.
    ///
    /// Rulesets are the exception. They are versioned, and a shipped ruleset whose
    /// version has gone up replaces the stored one. Without that, a database
    /// seeded before a rule existed keeps the old rules for ever: adding a
    /// preferred device for a new kind of circuit would leave every existing
    /// house generating nothing for it, with only a diagnostic to show for it.
    /// Rulesets cannot be edited in place through the API — an admin makes their
    /// own — so nobody's work is at stake.
    public static async Task<int> SeedAsync(
        HouseConfigDbContext db,
        SeedDocument seed,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var upgraded = 0;

        var existingDeviceIds = await db.DeviceTypes.Select(d => d.Id).ToListAsync(cancellationToken);
        foreach (var d in seed.DeviceTypes.Where(d => !existingDeviceIds.Contains(d.Id)))
        {
            db.DeviceTypes.Add(new DeviceTypeRow
            {
                Id = d.Id, Manufacturer = d.Manufacturer, Model = d.Model, PartNumber = d.PartNumber,
                Category = d.Category, ModuleWidth = d.ModuleWidth, ChannelCount = d.ChannelCount,
                MaxLoadPerChannelW = d.MaxLoadPerChannelW, MaxTotalLoadW = d.MaxTotalLoadW,
                Active = d.Active
            });
            inserted++;
        }

        var existingEnclosureIds = await db.Enclosures.Select(e => e.Id).ToListAsync(cancellationToken);
        foreach (var e in seed.Enclosures.Where(e => !existingEnclosureIds.Contains(e.Id)))
        {
            db.Enclosures.Add(new EnclosureTypeRow
            {
                Id = e.Id, Manufacturer = e.Manufacturer, Model = e.Model,
                Rows = e.Rows, SlotsPerRow = e.SlotsPerRow, IpRating = e.IpRating
            });
            inserted++;
        }

        var existingRuleSets = await db.RuleSets.ToDictionaryAsync(r => r.Id, cancellationToken);
        foreach (var r in seed.RuleSets)
        {
            var payload = JsonSerializer.Serialize(r.Payload, DomainMapper.Json);

            if (!existingRuleSets.TryGetValue(r.Id, out var stored))
            {
                db.RuleSets.Add(new RuleSetRow
                {
                    Id = r.Id, Name = r.Name, Version = r.Version, IsDefault = r.IsDefault,
                    PayloadJson = payload
                });
                inserted++;
                continue;
            }

            if (stored.Version >= r.Version) continue;

            stored.Name = r.Name;
            stored.Version = r.Version;
            stored.IsDefault = r.IsDefault;
            stored.PayloadJson = payload;
            upgraded++;
        }

        if (inserted > 0 || upgraded > 0) await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }
}
