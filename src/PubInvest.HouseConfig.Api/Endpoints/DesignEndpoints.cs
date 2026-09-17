using PubInvest.HouseConfig.Api.Contracts;
using PubInvest.HouseConfig.Api.Services;
using PubInvest.HouseConfig.Domain.Generation;

namespace PubInvest.HouseConfig.Api.Endpoints;

public static class DesignEndpoints
{
    public static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/submains/{id:guid}/design/preview", async (
                Guid id,
                PreviewRequest? request,
                DesignService service,
                CancellationToken ct) =>
            {
                var (inputs, error) = await service.LoadAsync(id, request, ct);
                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
                }

                if (inputs is null) return Results.NotFound();

                var result = PanelGenerator.Generate(inputs.Request);

                return Results.Ok(DesignService.ToResponse(
                    id, inputs.Submain.LayoutVersion, result, inputs.Circuits));
            })
            .WithTags("Design");

        app.MapPost("/submains/{id:guid}/design/generate", async (
                Guid id,
                PreviewRequest? request,
                DesignService service,
                CancellationToken ct) =>
            {
                var (response, error, hasErrors) = await service.GenerateAsync(id, request, ct);
                if (error is not null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["design"] = [error] });
                }

                if (response is null) return Results.NotFound();

                return hasErrors
                    ? Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity)
                    : Results.Ok(response);
            })
            .WithTags("Design");

        return app;
    }
}
