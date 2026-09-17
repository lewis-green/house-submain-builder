using Microsoft.EntityFrameworkCore;
using PubInvest.HouseConfig.Data.Entities;

namespace PubInvest.HouseConfig.Data.Tests;

[Collection("postgres")]
public class PersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_submain_with_circuits_and_devices_round_trips()
    {
        var projectId = Guid.NewGuid();
        var submainId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "Test House",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "test",
                Submains =
                [
                    new Submain
                    {
                        Id = submainId,
                        Name = "Ground Floor West",
                        LayoutVersion = 1,
                        Circuits = [new CircuitRow { Id = Guid.NewGuid(), Type = "DimmedLighting", Name = "Lighting 1", Sequence = 1 }],
                        Devices =
                        [
                            new DeviceInstance
                            {
                                Id = Guid.NewGuid(),
                                DeviceTypeId = Guid.NewGuid(),
                                Category = "Dimmer240",
                                RowIndex = 1, StartSlot = 0, ModuleWidth = 2,
                                Label = "Dimmer 1",
                                Channels = [new DeviceChannelRow { Id = Guid.NewGuid(), ChannelIndex = 0, IsSpare = true }]
                            }
                        ]
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var submain = await db.Submains
                .Include(s => s.Circuits)
                .Include(s => s.Devices).ThenInclude(d => d.Channels)
                .SingleAsync(s => s.Id == submainId);

            Assert.Single(submain.Circuits);
            Assert.Single(Assert.Single(submain.Devices).Channels);
        }
    }

    [Fact]
    public async Task Two_devices_cannot_occupy_the_same_slot_in_one_submain()
    {
        var deviceTypeId = Guid.NewGuid();

        await using var db = fixture.CreateContext();
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(), Name = "Clash House", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test",
            Submains =
            [
                new Submain
                {
                    Id = Guid.NewGuid(), Name = "Clash", LayoutVersion = 1,
                    Devices =
                    [
                        new DeviceInstance { Id = Guid.NewGuid(), DeviceTypeId = deviceTypeId, Category = "Relay", RowIndex = 0, StartSlot = 0, ModuleWidth = 4, Label = "Relay 1" },
                        new DeviceInstance { Id = Guid.NewGuid(), DeviceTypeId = deviceTypeId, Category = "Relay", RowIndex = 0, StartSlot = 0, ModuleWidth = 4, Label = "Relay 2" }
                    ]
                }
            ]
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
