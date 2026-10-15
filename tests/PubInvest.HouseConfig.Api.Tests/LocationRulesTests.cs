using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Api.Locations;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Api.Tests;

/// The refusals a save can meet when a location is removed between the
/// endpoint's check and its save. The race itself cannot be timed from a test,
/// so these drive the database into each refusal directly and check that it is
/// recognised for what it is — the part the endpoints' answers rest on.
[Collection("api")]
public class LocationRulesTests(HouseConfigApiFactory factory)
{
    private static Project House() => new()
    {
        Id = Guid.NewGuid(), Name = $"Rules {Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test",
    };

    [Fact]
    public async Task A_panel_saved_into_a_location_that_has_gone_is_recognised_as_misplaced()
    {
        var house = House();
        await using var db = factory.NewDbContext();
        db.Projects.Add(house);
        db.Submains.Add(new Submain { Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Ground floor", LocationId = Guid.NewGuid() });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.True(LocationRules.IsMissingLocation(refused));
    }

    [Fact]
    public async Task A_rack_saved_into_a_location_that_has_gone_is_recognised_as_misplaced()
    {
        await using (var seed = factory.NewDbContext())
        {
            await CatalogueSeeder.SeedAsync(seed, TestSeed.Document(), CancellationToken.None);
        }

        var house = House();
        await using var db = factory.NewDbContext();
        db.Projects.Add(house);
        await db.SaveChangesAsync();
        db.Racks.Add(new Rack
        {
            Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Comms", RackTypeId = TestSeed.Cabinet4UId,
            LocationId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
        });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.True(LocationRules.IsMissingLocation(refused));
    }

    [Fact]
    public async Task Another_missing_reference_is_not_taken_for_a_missing_location()
    {
        var house = House();
        await using var db = factory.NewDbContext();
        db.Projects.Add(house);
        await db.SaveChangesAsync();
        db.Racks.Add(new Rack
        {
            Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Comms", RackTypeId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
        });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.False(LocationRules.IsMissingLocation(refused));
    }

    /// The rename's half of the race: a location removed after it was read
    /// cannot be saved, and EF says so with a concurrency exception, which the
    /// endpoint answers with a 404.
    [Fact]
    public async Task Renaming_a_location_removed_since_it_was_read_fails_as_a_concurrency_conflict()
    {
        var house = House();
        var location = new Location { Id = Guid.NewGuid(), ProjectId = house.Id, Name = "Garage", CreatedAt = DateTimeOffset.UtcNow };
        await using (var setup = factory.NewDbContext())
        {
            setup.Projects.Add(house);
            setup.Locations.Add(location);
            await setup.SaveChangesAsync();
        }

        await using var db = factory.NewDbContext();
        var read = await db.Locations.SingleAsync(l => l.Id == location.Id);
        await using (var other = factory.NewDbContext())
        {
            await other.Locations.Where(l => l.Id == location.Id).ExecuteDeleteAsync();
        }
        read.Name = "Workshop";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
}
