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

                // Just issued from the submain as it is, so it is current by definition.
                return Results.Created($"/revisions/{revision.Id}", new
                {
                    revision.Id,
                    revision.SubmainId,
                    Number = await revisions.NumberOfAsync(revision, ct),
                    revision.LayoutVersion,
                    revision.IssuedAt,
                    revision.IssuedBy,
                    Current = true,
                });
            })
            .WithTags("Revisions");

        // `current` says whether each PDF still matches the submain now — decided
        // here, by comparing snapshots, so the UI never has to guess which edits
        // a drawing or schedule would show.
        app.MapGet("/submains/{id:guid}/revisions", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var isCurrent = await revisions.CurrentCheckAsync(id, ct);
                var list = await revisions.ListAsync(id, ct);
                // Newest first, so the number counts down from the total: the
                // server says which revision this is, rather than a client
                // counting what it happened to receive.
                return list.Select((r, i) => new
                {
                    r.Id, r.SubmainId, Number = list.Count - i, r.LayoutVersion, r.IssuedAt, r.IssuedBy, Current = isCurrent(r),
                });
            })
            .WithTags("Revisions");

        app.MapGet("/revisions/{id:guid}", async (
                Guid id,
                RevisionService revisions,
                CancellationToken ct) =>
            {
                var revision = await revisions.FindAsync(id, ct);
                if (revision is null) return Results.NotFound();

                var isCurrent = await revisions.CurrentCheckAsync(revision.SubmainId, ct);
                return Results.Ok(new
                {
                    revision.Id,
                    revision.SubmainId,
                    Number = await revisions.NumberOfAsync(revision, ct),
                    revision.LayoutVersion,
                    revision.IssuedAt,
                    revision.IssuedBy,
                    Current = isCurrent(revision),
                    revision.SnapshotJson,
                });
            })
            .WithTags("Revisions");

        return app;
    }
}
