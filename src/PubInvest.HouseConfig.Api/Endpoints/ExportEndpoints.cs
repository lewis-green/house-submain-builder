using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Documents;
using PubInvest.HouseConfig.Api.Revisions;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Domain.Bom;
using QuestPDF.Fluent;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        // Renders the latest issued revision, issuing one first if there is none.
        // A PDF is never rendered from live tables: a drawing has to mean what it
        // meant the day it was printed.
        app.MapGet("/submains/{id:guid}/export/pdf", async (
                Guid id,
                RevisionService revisions,
                HttpContext http,
                CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);

                if (revision is null)
                {
                    var (issued, error) = await revisions.IssueAsync(
                        id, http.User.Identity?.Name ?? "anonymous", ct);

                    if (error is not null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["export"] = [error] });
                    }

                    if (issued is null) return Results.NotFound();
                    revision = issued;
                }

                return Pdf(revision);
            })
            .WithTags("Export");

        app.MapGet("/revisions/{id:guid}/export/pdf", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.FindAsync(id, ct);
                return revision is null ? Results.NotFound() : Pdf(revision);
            })
            .WithTags("Export");

        app.MapGet("/submains/{id:guid}/bom", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RevisionService.Read(revision);
                return Results.Ok(Describe(snapshot.Bom));
            })
            .WithTags("Export");

        app.MapGet("/submains/{id:guid}/bom.csv", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.LatestAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var snapshot = RevisionService.Read(revision);
                var csv = BomCsv.Write(snapshot.Bom, $"{snapshot.ProjectName} — {snapshot.SubmainName}");

                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                    $"{Slug(snapshot.SubmainName)}-bom.csv");
            })
            .WithTags("Export");

        app.MapGet("/projects/{id:guid}/bom", async (
                Guid id,
                HouseConfigDbContext db,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var (bom, error) = await HouseBom(id, db, revisions, ct);
                if (error is not null) return Results.NotFound();
                return Results.Ok(Describe(bom!));
            })
            .WithTags("Export");

        app.MapGet("/projects/{id:guid}/bom.csv", async (
                Guid id,
                HouseConfigDbContext db,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var (bom, error) = await HouseBom(id, db, revisions, ct);
                if (error is not null) return Results.NotFound();

                var project = await db.Projects.SingleAsync(p => p.Id == id, ct);
                var csv = BomCsv.Write(bom!, project.Name);

                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"{Slug(project.Name)}-bom.csv");
            })
            .WithTags("Export");

        return app;
    }

    /// A house BOM sums each submain's latest *issued* revision, so it costs what
    /// was issued rather than whatever is being edited right now.
    private static async Task<(BillOfMaterials? Bom, string? Error)> HouseBom(
        Guid projectId,
        HouseConfigDbContext db,
        RevisionService revisions,
        CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return (null, "No such house.");

        var submainIds = await db.Submains
            .Where(s => s.ProjectId == projectId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var lines = new List<BomLine>();

        foreach (var submainId in submainIds)
        {
            var revision = await revisions.LatestAsync(submainId, ct);
            if (revision is null) continue;
            lines.AddRange(RevisionService.Read(revision).Bom.Lines);
        }

        var merged = lines
            .GroupBy(l => l.CatalogueId)
            .Select(g => new BomLine(
                g.Key,
                g.First().PartNumber,
                g.First().Description,
                g.Sum(l => l.Quantity),
                g.First().UnitCost))
            .OrderBy(l => l.PartNumber, StringComparer.Ordinal)
            .ToList();

        return (new BillOfMaterials(merged), null);
    }

    private static object Describe(BillOfMaterials bom)
    {
        var unpriced = bom.Lines.Count(l => l.UnitCost <= 0m);

        return new
        {
            lines = bom.Lines.Select(l => new
            {
                l.CatalogueId, l.PartNumber, l.Description, l.Quantity, l.UnitCost, l.LineTotal,
            }),
            total = unpriced > 0 ? (decimal?)null : bom.Total,
            unpricedLines = unpriced,
            priced = unpriced == 0,
        };
    }

    private static IResult Pdf(Data.Entities.PanelRevision revision)
    {
        var snapshot = RevisionService.Read(revision);
        var bytes = new PanelDocument(snapshot, revision.IssuedAt, revision.IssuedBy, snapshot.Bom).GeneratePdf();

        return Results.File(bytes, "application/pdf", $"{Slug(snapshot.SubmainName)}-panel.pdf");
    }

    private static string Slug(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
