using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;

namespace PubInvest.HouseConfig.Data.Seeding;

public static class CatalogueSeeder
{
    /// Additive and idempotent: a row whose id already exists is left untouched,
    /// so an admin's edit is never overwritten by a redeploy.
    public static async Task<int> SeedAsync(
        HouseConfigDbContext db,
        SeedDocument seed,
        CancellationToken cancellationToken)
    {
        var inserted = 0;

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

        var existingRuleSetIds = await db.RuleSets.Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var r in seed.RuleSets.Where(r => !existingRuleSetIds.Contains(r.Id)))
        {
            db.RuleSets.Add(new RuleSetRow
            {
                Id = r.Id, Name = r.Name, Version = r.Version, IsDefault = r.IsDefault,
                PayloadJson = JsonSerializer.Serialize(r.Payload, DomainMapper.Json)
            });
            inserted++;
        }

        if (inserted > 0) await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }
}
