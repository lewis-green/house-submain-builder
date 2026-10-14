using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data.Tests;

[Collection("postgres")]
public class LocationPersistenceTests(PostgresFixture fixture)
{
    private static Project House(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test",
    };

    private static Location Location(Guid projectId, string name, int sequence = 0) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, Name = name, Sequence = sequence, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task A_house_cannot_have_two_locations_with_the_same_name_in_any_case()
    {
        var house = House("Twin Rooms");

        await using var db = fixture.CreateContext();
        db.Projects.Add(house);
        db.Locations.Add(Location(house.Id, "Plant room"));
        await db.SaveChangesAsync();

        db.Locations.Add(Location(house.Id, "PLANT ROOM", 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_houses_can_each_have_a_plant_room()
    {
        var first = House("First");
        var second = House("Second");

        await using var db = fixture.CreateContext();
        db.Projects.AddRange(first, second);
        db.Locations.AddRange(Location(first.Id, "Plant room"), Location(second.Id, "Plant room"));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Removing_a_location_leaves_its_panel_unplaced()
    {
        var house = House("Moving House");
        var location = Location(house.Id, "Garage");
        var submainId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Projects.Add(house);
            db.Locations.Add(location);
            db.Submains.Add(new Submain { Id = submainId, ProjectId = house.Id, Name = "Garage panel", LocationId = location.Id });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            await db.Locations.Where(l => l.Id == location.Id).ExecuteDeleteAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var submain = await db.Submains.SingleAsync(s => s.Id == submainId);
            Assert.Null(submain.LocationId);
        }
    }

    [Fact]
    public async Task Removing_a_house_removes_its_locations()
    {
        var house = House("Demolished");
        var location = Location(house.Id, "Cellar");

        await using (var db = fixture.CreateContext())
        {
            db.Projects.Add(house);
            db.Locations.Add(location);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            await db.Projects.Where(p => p.Id == house.Id).ExecuteDeleteAsync();
            Assert.False(await db.Locations.AnyAsync(l => l.Id == location.Id));
        }
    }
}
