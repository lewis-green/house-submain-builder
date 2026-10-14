using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Api.Racks;
using QuestPDF.Fluent;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RackRevisionEndpoints
{
    public static IEndpointRouteBuilder MapRackRevisionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/racks/{id:guid}/revisions", async (
                Guid id,
                RackRevisionService revisions,
                HttpContext http,
                CancellationToken ct) =>
            {
                var (revision, error) = await revisions.IssueAsync(id, http.User.Identity?.Name ?? "anonymous", ct);

                // A refusal the user can act on (an empty rack, an accessory gone
                // from the catalogue) is 422 in plain words, as every rack refusal
                // is, so the issue button can show the reason rather than a
                // generic validation message.
                if (error is not null)
                {
                    return Results.Json(new RefusalResponse(error), statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                if (revision is null) return Results.NotFound();

                // Just issued from the rack as it is, so current by definition.
                return Results.Created($"/rack-revisions/{revision.Id}", new
                {
                    revision.Id,
                    revision.RackId,
                    Number = await revisions.NumberOfAsync(revision, ct),
                    revision.LayoutVersion,
                    revision.IssuedAt,
                    revision.IssuedBy,
                    Current = true,
                });
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/revisions", async (
                Guid id,
                RackRevisionService revisions,
                CancellationToken ct) =>
            {
                var isCurrent = await revisions.CurrentCheckAsync(id, ct);
                var list = await revisions.ListAsync(id, ct);
                return list.Select((r, i) => new
                {
                    r.Id, r.RackId, Number = list.Count - i, r.LayoutVersion, r.IssuedAt, r.IssuedBy, Current = isCurrent(r),
                });
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/bom", async (Guid id, RackRevisionService revisions, CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                return revision is null
                    ? Results.NotFound()
                    : Results.Ok(ExportEndpoints.Describe(RackRevisionService.Read(revision).Bom));
            })
            .WithTags("Rack revisions");

        app.MapGet("/racks/{id:guid}/bom.csv", async (Guid id, RackRevisionService revisions, CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RackRevisionService.Read(revision);
                var csv = BomCsv.Write(snapshot.Bom, $"{snapshot.ProjectName} — {snapshot.RackName}");
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                    $"{ExportEndpoints.Slug(snapshot.RackName)}-bom.csv");
            })
            .WithTags("Rack revisions");

        app.MapGet("/rack-revisions/{id:guid}/export/pdf", async (
                Guid id,
                RackRevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.FindAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RackRevisionService.Read(revision);
                var bytes = new RackDocument(
                        snapshot, await revisions.NumberOfAsync(revision, ct), revision.IssuedAt, revision.IssuedBy)
                    .GeneratePdf();

                return Results.File(bytes, "application/pdf", $"{ExportEndpoints.Slug(snapshot.RackName)}-rack.pdf");
            })
            .WithTags("Rack revisions");

        return app;
    }
}
