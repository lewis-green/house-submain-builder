using System.Text.Json;
using System.Text.Json.Nodes;
using PubInvest.HouseConfig.Data.Entities;
using PubInvest.HouseConfig.Data.Mapping;
using PubInvest.HouseConfig.Data.Seeding;

namespace PubInvest.HouseConfig.Data.Tests;

/// Rulesets already in a database were written before line bars existed (and
/// while the terminal rules still carried a retired field). They must keep reading.
public class RuleSetPayloadCompatibilityTests
{
    [Fact]
    public void A_ruleset_stored_before_line_bars_still_reads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "catalogue.v1.json");
        var seed = JsonSerializer.Deserialize<SeedDocument>(File.ReadAllText(path), DomainMapper.Json)!;
        var current = seed.RuleSets.Single(r => r.IsDefault).Payload;

        var node = JsonSerializer.SerializeToNode(current, DomainMapper.Json)!;
        var terminals = node["terminals"]!.AsObject();
        terminals["blocksPerCircuit"] = 1;
        terminals.Remove("lineBridgeBarDeviceTypeId");
        terminals.Remove("lineBridgeBarWays");

        var read = DomainMapper.ToDomain(new RuleSetRow { Name = "old", Version = 1, PayloadJson = node.ToJsonString() });

        Assert.Equal(0, read.Terminals.LineBridgeBarWays);
        Assert.Equal(Guid.Empty, read.Terminals.LineBridgeBarDeviceTypeId);
        Assert.Equal(current.Terminals.DeviceTypeId, read.Terminals.DeviceTypeId);
        Assert.Equal(current.Terminals.BridgeBarDeviceTypeId, read.Terminals.BridgeBarDeviceTypeId);
        Assert.Equal(current.Terminals.BridgeBarWays, read.Terminals.BridgeBarWays);
        Assert.Equal(current.Terminals.EndStopDeviceTypeId, read.Terminals.EndStopDeviceTypeId);
        Assert.Equal(current.Terminals.EndStopsPerBank, read.Terminals.EndStopsPerBank);
    }
}
