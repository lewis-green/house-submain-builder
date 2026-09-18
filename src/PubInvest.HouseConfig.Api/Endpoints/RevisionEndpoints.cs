using PubInvest.HouseConfig.Api.Revisions;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class RevisionEndpoints
{
    public static IEndpointRouteBuilder MapRevisionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/submains/{id:guid}/revisions", async (
                Guid id,
                RevisionService revisions,
                HttpContext http,
                CancellationToken ct) =>
            {
                var (revision, error) = await revisions.IssueAsync(
                    id, http.User.Identity?.Name ?? "anonymous", ct);

                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["revision"] = [error] });
                }

                if (revision is null) return Results.NotFound();

                return Results.Created($"/revisions/{revision.Id}", new
                {
                    revision.Id,
                    revision.SubmainId,
                    revision.LayoutVersion,
                    revision.IssuedAt,
                    revision.IssuedBy,
                });
            })
            .WithTags("Revisions");

        app.MapGet("/submains/{id:guid}/revisions", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            (await revisions.ListAsync(id, ct))
                .Select(r => new { r.Id, r.SubmainId, r.LayoutVersion, r.IssuedAt, r.IssuedBy }))
            .WithTags("Revisions");

        app.MapGet("/revisions/{id:guid}", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.FindAsync(id, ct);
                return revision is null
                    ? Results.NotFound()
                    : Results.Ok(new
                    {
                        revision.Id,
                        revision.SubmainId,
                        revision.LayoutVersion,
                        revision.IssuedAt,
                        revision.IssuedBy,
                        revision.SnapshotJson,
                    });
            })
            .WithTags("Revisions");

        return app;
    }
}
