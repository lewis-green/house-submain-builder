using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalogue").WithTags("Catalogue");

        group.MapGet("/device-types", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.DeviceTypes.OrderBy(d => d.PartNumber).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));

        group.MapGet("/enclosures", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.Enclosures.OrderBy(e => e.Model).ToListAsync(ct))
                .Select(DomainMapper.ToDomain));

        group.MapGet("/rulesets", async (HouseConfigDbContext db, CancellationToken ct) =>
            (await db.RuleSets.OrderBy(r => r.Name).ThenBy(r => r.Version).ToListAsync(ct))
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.Version,
                    r.IsDefault,
                    Payload = DomainMapper.ToDomain(r)
                }));

        return app;
    }
}
