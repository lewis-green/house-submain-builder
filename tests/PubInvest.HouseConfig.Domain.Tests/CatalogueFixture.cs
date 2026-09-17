using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Tests;

/// Arbitrary but internally consistent test data. The real catalogue is seeded
/// separately and must never be inferred from this file.
public static class CatalogueFixture
{
    public static readonly Guid DimmerId   = new("11111111-0000-0000-0000-000000000001");
    public static readonly Guid TapeDimId  = new("11111111-0000-0000-0000-000000000002");
    public static readonly Guid RelayId    = new("11111111-0000-0000-0000-000000000003");
    public static readonly Guid Psu100Id   = new("11111111-0000-0000-0000-000000000004");
    public static readonly Guid Psu240Id   = new("11111111-0000-0000-0000-000000000005");
    public static readonly Guid TerminalId = new("11111111-0000-0000-0000-000000000006");
    public static readonly Guid EarthId    = new("11111111-0000-0000-0000-000000000007");
    public static readonly Guid BridgeId   = new("11111111-0000-0000-0000-000000000008");
    public static readonly Guid EndStopId  = new("11111111-0000-0000-0000-000000000009");
    public static readonly Guid SmallBoxId = new("22222222-0000-0000-0000-000000000001");
    public static readonly Guid LargeBoxId = new("22222222-0000-0000-0000-000000000002");

    public static DeviceCatalogue Catalogue() => new(
    [
        new DeviceType(DimmerId,   "Shelly",    "Pro Dimmer 2PM",     "TEST-DIM2",   DeviceCategory.Dimmer240,   2, 2, 200,  400,  60.00m, true),
        new DeviceType(TapeDimId,  "Shelly",    "Pro Dimmer 0/1-10V", "TEST-DIM10",  DeviceCategory.Dimmer0_10V, 2, 2, null, null, 55.00m, true),
        new DeviceType(RelayId,    "Shelly",    "Pro 4PM",            "TEST-REL4",   DeviceCategory.Relay,       4, 4, 3680, 7360, 95.00m, true),
        new DeviceType(Psu100Id,   "Mean Well", "DR-100-24",          "TEST-PSU100", DeviceCategory.Psu24V,      3, 0, null, 100,  45.00m, true),
        new DeviceType(Psu240Id,   "Mean Well", "DR-240-24",          "TEST-PSU240", DeviceCategory.Psu24V,      6, 0, null, 240,  85.00m, true),
        new DeviceType(TerminalId, "WAGO",      "TOPJOB S 2.5",       "TEST-TB",     DeviceCategory.Terminal240, 1, 0, null, null,  1.50m, true),
        new DeviceType(EarthId,    "WAGO",      "TOPJOB S 2.5 PE",    "TEST-TBPE",   DeviceCategory.Terminal240, 1, 0, null, null,  2.10m, true),
        new DeviceType(BridgeId,   "WAGO",      "Jumper bar 10-way",  "TEST-BAR",    DeviceCategory.Accessory,   0, 0, null, null,  3.00m, true),
        new DeviceType(EndStopId,  "WAGO",      "End stop",           "TEST-STOP",   DeviceCategory.Accessory,   0, 0, null, null,  0.80m, true)
    ]);

    public static EnclosureType SmallEnclosure() =>
        new(SmallBoxId, "Hager", "Test 2x12", 2, 12, "IP30", 90.00m);

    public static EnclosureType LargeEnclosure() =>
        new(LargeBoxId, "Hager", "Test 6x24", 6, 24, "IP30", 220.00m);

    public static IReadOnlyList<EnclosureType> AllEnclosures() =>
        [SmallEnclosure(), LargeEnclosure()];

    public static RuleSetPayload Rules() => new(
        BandOrder: [DeviceCategory.Terminal240, DeviceCategory.Dimmer240, DeviceCategory.Relay, DeviceCategory.Psu24V],
        BandStartsNewRow: true,
        PsuDeratingFactor: 0.8m,
        PreferredDevice: new PreferredDevices(DimmerId, TapeDimId, RelayId, [Psu240Id, Psu100Id]),
        Terminals: new TerminalRules(
            Line:    new TerminalConductorRule(TerminalId, 1, Bridged: false),
            Neutral: new TerminalConductorRule(TerminalId, 1, Bridged: true),
            Earth:   new TerminalConductorRule(EarthId,    1, Bridged: true),
            BridgeBarDeviceTypeId: BridgeId,
            BridgeBarWays: 10,
            EndStopDeviceTypeId: EndStopId,
            EndStopsPerBank: 2),
        Packing: "firstFit");
}
