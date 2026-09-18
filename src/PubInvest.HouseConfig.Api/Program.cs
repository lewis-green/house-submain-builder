using System.Text.Json;
using System.Text.Json.Serialization;
using Keycloak.AuthServices.Authentication;
using PubInvest.HouseConfig.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;

// QuestPDF Community licence, set once before any document is generated.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var authEnabled = builder.Configuration.GetValue("HouseConfig:AuthEnabled", true);

builder.Services.AddDbContext<HouseConfigDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("HouseConfig")));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddScoped<PubInvest.HouseConfig.Api.Services.DesignService>();
builder.Services.AddScoped<PubInvest.HouseConfig.Api.Revisions.RevisionService>();
builder.Services.AddHealthChecks().AddDbContextCheck<HouseConfigDbContext>();
builder.Services.AddOpenApi();

if (authEnabled)
{
    builder.Services.AddKeycloakWebApiAuthentication(builder.Configuration);
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("catalogue-admin", policy => policy.RequireRole("catalogue-admin"));
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("HouseConfig:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health");
app.MapOpenApi();

app.MapProjectEndpoints();
app.MapSubmainEndpoints();
app.MapCatalogueEndpoints();
app.MapDesignEndpoints();
app.MapCircuitEndpoints();
app.MapDeviceEndpoints();
app.MapRevisionEndpoints();
app.MapExportEndpoints();
app.MapCatalogueAdminEndpoints(authEnabled);

if (builder.Configuration.GetValue("HouseConfig:SeedOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HouseConfigDbContext>();
    await db.Database.MigrateAsync();

    var seedPath = Path.Combine(AppContext.BaseDirectory, "seed", "catalogue.v1.json");
    if (File.Exists(seedPath))
    {
        var seed = JsonSerializer.Deserialize<SeedDocument>(
            await File.ReadAllTextAsync(seedPath),
            DomainMapper.Json)!;
        await CatalogueSeeder.SeedAsync(db, seed, CancellationToken.None);
    }
    else
    {
        app.Logger.LogWarning(
            "No catalogue seed at {SeedPath}; the catalogue will be empty until one is supplied.",
            seedPath);
    }
}

app.Run();

public partial class Program;
