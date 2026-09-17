using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace PubInvest.HouseConfig.Data.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public HouseConfigDbContext CreateContext()
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
