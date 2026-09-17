using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/projects").WithTags("Projects");

        group.MapGet("/", async (HouseConfigDbContext db, CancellationToken ct) =>
            await db.Projects
                .OrderBy(p => p.Name)
                .Select(p => new ProjectResponse(p.Id, p.Name, p.Address, p.Notes, p.Submains.Count))
                .ToListAsync(ct));

        group.MapGet("/{id:guid}", async (Guid id, HouseConfigDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects
                .Where(p => p.Id == id)
                .Select(p => new ProjectResponse(p.Id, p.Name, p.Address, p.Notes, p.Submains.Count))
                .SingleOrDefaultAsync(ct);

            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        group.MapPost("/", async (
            CreateProjectRequest request,
            HouseConfigDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["A project name is required."]
                });
            }

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Address = request.Address,
                Notes = request.Notes,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = http.User.Identity?.Name ?? "anonymous"
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/projects/{project.Id}",
                new ProjectResponse(project.Id, project.Name, project.Address, project.Notes, 0));
        });

        return app;
    }
}
