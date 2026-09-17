using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubInvest.HouseConfig.Data;
using Testcontainers.PostgreSql;

namespace PubInvest.HouseConfig.Api.Tests;

public class HouseConfigApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HouseConfig", _postgres.GetConnectionString());
        builder.UseSetting("HouseConfig:AuthEnabled", "false");
        builder.UseSetting("HouseConfig:SeedOnStartup", "false");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HouseConfigDbContext>().Database.MigrateAsync();
    }

    public HouseConfigDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<HouseConfigApiFactory>;
