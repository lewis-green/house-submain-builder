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
    public static readonly Guid IsolatorId = new("11111111-0000-0000-0000-000000000007");
    public static readonly Guid CoverId    = new("11111111-0000-0000-0000-00000000000c");
    public static readonly Guid RgbwId     = new("11111111-0000-0000-0000-00000000000d");
    public static readonly Guid MeterId    = new("11111111-0000-0000-0000-00000000000e");
    public static readonly Guid LanId      = new("11111111-0000-0000-0000-00000000000f");
    public static readonly Guid DcPlusId   = new("11111111-0000-0000-0000-00000000000a");
    public static readonly Guid DcMinusId  = new("11111111-0000-0000-0000-00000000000b");
    public static readonly Guid BridgeId   = new("11111111-0000-0000-0000-000000000008");
    public static readonly Guid EndStopId  = new("11111111-0000-0000-0000-000000000009");
    public static readonly Guid SmallBoxId = new("22222222-0000-0000-0000-000000000001");
    public static readonly Guid LargeBoxId = new("22222222-0000-0000-0000-000000000002");

    public static DeviceCatalogue Catalogue() => new(
    [
        new DeviceType(DimmerId,   "Shelly",    "Pro Dimmer 2PM",     "TEST-DIM2",   DeviceCategory.Dimmer240,   2, 2, 200,  400, true),
        new DeviceType(TapeDimId,  "Shelly",    "Pro Dimmer 0/1-10V", "TEST-DIM10",  DeviceCategory.Dimmer0_10V, 2, 2, null, null, true),
        new DeviceType(RelayId,    "Shelly",    "Pro 4PM",            "TEST-REL4",   DeviceCategory.Relay,       4, 4, 3680, 7360, true),
        new DeviceType(CoverId,    "Shelly",    "Pro Dual Cover PM",  "TEST-COVER",  DeviceCategory.Cover,       4, 2, null, null, true),
        new DeviceType(RgbwId,     "Shelly",    "Pro RGBWW PM",       "TEST-RGBWW",  DeviceCategory.LedController, 4, 5, null, null, true),
        new DeviceType(MeterId,    "Shelly",    "Pro 3EM",            "TEST-3EM",    DeviceCategory.EnergyMeter, 6, 3, null, null, true),
        new DeviceType(LanId,      "Shelly",    "LAN Switch",         "TEST-LAN",    DeviceCategory.Network,     4, 5, null, null, true),
        new DeviceType(Psu100Id,   "Mean Well", "DR-100-24",          "TEST-PSU100", DeviceCategory.ExternalDriver, 0, 0, null, 100, true),
        new DeviceType(Psu240Id,   "Mean Well", "DR-240-24",          "TEST-PSU240", DeviceCategory.ExternalDriver, 0, 0, null, 240, true),
        new DeviceType(IsolatorId, "Test",      "2-pole isolator",    "TEST-ISO",    DeviceCategory.Isolator,    6, 0, null, null, true),
        new DeviceType(DcPlusId,   "WAGO",      "12-way +24V",        "TEST-DC+",    DeviceCategory.Dc24VPositive, 4, 12, null, null, true),
        new DeviceType(DcMinusId,  "WAGO",      "12-way -24V",        "TEST-DC-",    DeviceCategory.Dc24VNegative, 4, 12, null, null, true),
        new DeviceType(TerminalId, "WAGO",      "TOPJOB S 2.5",       "TEST-TB",     DeviceCategory.Terminal240, 1, 0, null, null, true),
        new DeviceType(BridgeId,   "WAGO",      "Jumper bar 10-way",  "TEST-BAR",    DeviceCategory.Accessory,   0, 0, null, null, true),
        new DeviceType(EndStopId,  "WAGO",      "End stop",           "TEST-STOP",   DeviceCategory.Accessory,   0, 0, null, null, true)
    ]);

    public static EnclosureType SmallEnclosure() =>
        new(SmallBoxId, "Hager", "Test 2x12", 2, 12, "IP30");

    public static EnclosureType LargeEnclosure() =>
        new(LargeBoxId, "Hager", "Test 6x24", 6, 24, "IP30");

    public static IReadOnlyList<EnclosureType> AllEnclosures() =>
        [SmallEnclosure(), LargeEnclosure()];

    /// Termination stays on the top row whatever else moves: circuit terminals
    /// then the 24V pair from the left, the isolator hard against the right.
    /// Covers and LED controllers from the left, meters and network gear from
    /// the right. Kept in step with seed/catalogue.v1.json by hand — a fixture
    /// that drifts from the seed hides real behaviour behind passing tests.
    private static PackingZone Extras => new(
        FromLeft: [DeviceCategory.Cover, DeviceCategory.LedController],
        FromRight: [DeviceCategory.EnergyMeter, DeviceCategory.Network]);

    private static PackingZone Termination => new(
        FromLeft: [DeviceCategory.Terminal240, DeviceCategory.Dc24VPositive, DeviceCategory.Dc24VNegative],
        FromRight: [DeviceCategory.Isolator]);

    public static RuleSetPayload Rules() => new(
        Layouts:
        [
            // Finest: a row for every kind of device.
            new PanelLayoutOption(
            [
                Termination,
                new PackingZone([DeviceCategory.Dimmer240], []),
                new PackingZone([DeviceCategory.Dimmer0_10V], []),
                new PackingZone([], [DeviceCategory.Relay]),
                Extras,
            ]),

            // Then: the two sorts of dimmer share a row, but from opposite ends
            // rather than one running straight on from the other. Relays keep
            // their own row.
            new PanelLayoutOption(
            [
                Termination,
                new PackingZone([DeviceCategory.Dimmer240], [DeviceCategory.Dimmer0_10V]),
                new PackingZone([], [DeviceCategory.Relay]),
                Extras,
            ]),

            // Then: the new gear joins the relay rail rather than everything
            // being merged onto one.
            new PanelLayoutOption(
            [
                Termination,
                new PackingZone([DeviceCategory.Dimmer240], [DeviceCategory.Dimmer0_10V]),
                new PackingZone(
                    [DeviceCategory.Cover, DeviceCategory.LedController],
                    [DeviceCategory.Relay, DeviceCategory.EnergyMeter, DeviceCategory.Network]),
            ]),

            // Last resort: all three on one row. A row has only two ends, so the
            // two sorts of dimmer do share the left one here.
            new PanelLayoutOption(
            [
                Termination,
                new PackingZone(
                    [DeviceCategory.Dimmer240, DeviceCategory.Dimmer0_10V,
                     DeviceCategory.Cover, DeviceCategory.LedController],
                    [DeviceCategory.Relay, DeviceCategory.EnergyMeter, DeviceCategory.Network]),
            ]),
        ],
        PsuDeratingFactor: 0.8m,
        PreferredDevice: new PreferredDevices(
            Isolator: IsolatorId,
            Dimmer240: DimmerId,
            Dimmer0_10V: TapeDimId,
            Relay: RelayId,
            Cover: CoverId,
            LedController: RgbwId,
            Dc24VPositive: DcPlusId,
            Dc24VNegative: DcMinusId,
            ExternalDriver: [Psu240Id, Psu100Id]),
        Terminals: new TerminalRules(
            DeviceTypeId: TerminalId,
            BlocksPerCircuit: 1,
            BridgeBarDeviceTypeId: BridgeId,
            BridgeBarWays: 10,
            EndStopDeviceTypeId: EndStopId,
            EndStopsPerBank: 2));
}
