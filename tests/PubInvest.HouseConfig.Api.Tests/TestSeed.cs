using PubInvest.HouseConfig.Data.Seeding;
using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Api.Tests;

/// Built in code rather than read from seed/catalogue.v1.json, so API tests never
/// depend on the real catalogue's still-unconfirmed module widths.
public static class TestSeed
{
    public static readonly Guid DimmerId   = new("aaaa0000-0000-4000-8000-000000000001");
    public static readonly Guid TapeDimId  = new("aaaa0000-0000-4000-8000-000000000002");
    public static readonly Guid RelayId    = new("aaaa0000-0000-4000-8000-000000000003");
    public static readonly Guid Psu240Id   = new("aaaa0000-0000-4000-8000-000000000004");
    public static readonly Guid Psu100Id   = new("aaaa0000-0000-4000-8000-000000000005");
    public static readonly Guid TerminalId = new("aaaa0000-0000-4000-8000-000000000006");
    public static readonly Guid IsolatorId = new("aaaa0000-0000-4000-8000-000000000007");
    public static readonly Guid DcPosId    = new("aaaa0000-0000-4000-8000-00000000000a");
    public static readonly Guid DcNegId    = new("aaaa0000-0000-4000-8000-00000000000b");
    public static readonly Guid BridgeId   = new("aaaa0000-0000-4000-8000-000000000008");
    public static readonly Guid EndStopId  = new("aaaa0000-0000-4000-8000-000000000009");
    public static readonly Guid CoverId    = new("aaaa0000-0000-4000-8000-00000000000c");
    public static readonly Guid RgbwId     = new("aaaa0000-0000-4000-8000-00000000000d");
    public static readonly Guid MeterId    = new("aaaa0000-0000-4000-8000-00000000000e");
    public static readonly Guid LanId      = new("aaaa0000-0000-4000-8000-00000000000f");

    public static readonly Guid EnclosureId     = new("bbbb0000-0000-4000-8000-000000000001");
    public static readonly Guid TinyEnclosureId = new("bbbb0000-0000-4000-8000-000000000002");
    public static readonly Guid RuleSetId       = new("cccc0000-0000-4000-8000-000000000001");

    /// Termination stays on the top row whatever else moves: circuit terminals
    /// then the 24V pair from the left, the isolator hard against the right.
    private static PackingZone Termination => new(
        FromLeft: [DeviceCategory.Terminal240, DeviceCategory.Dc24VPositive, DeviceCategory.Dc24VNegative],
        FromRight: [DeviceCategory.Isolator]);

    /// Covers and LED controllers from the left, meters and network gear from
    /// the right — the same rail the shipped seed gives them.
    private static PackingZone Extras => new(
        FromLeft: [DeviceCategory.Cover, DeviceCategory.LedController],
        FromRight: [DeviceCategory.EnergyMeter, DeviceCategory.Network]);

    public static SeedDocument Document() => new(
        Version: 1,
        DeviceTypes:
        [
            new SeedDeviceType(DimmerId,   "Shelly", "Test Dimmer 2",   "T-DIM2",   "Dimmer240",   2, 2, 200, 400, true),
            new SeedDeviceType(TapeDimId,  "Shelly", "Test Dimmer 10V", "T-DIM10",  "Dimmer0_10V", 2, 2, null, null, true),
            new SeedDeviceType(RelayId,    "Shelly", "Test Relay 4",    "T-REL4",   "Relay",       4, 4, 3680, 7360, true),
            new SeedDeviceType(Psu240Id,   "Test",   "Driver 240",      "T-PSU240", "ExternalDriver", 0, 0, null, 240, true),
            new SeedDeviceType(Psu100Id,   "Test",   "Driver 100",      "T-PSU100", "ExternalDriver", 0, 0, null, 100, true),
            new SeedDeviceType(IsolatorId, "Test",   "2-pole isolator", "T-ISO",    "Isolator",    6, 0, null, null, true),
            new SeedDeviceType(DcPosId,    "WAGO",   "12-way +24V",     "T-DC+",    "Dc24VPositive", 4, 12, null, null, true),
            new SeedDeviceType(DcNegId,    "WAGO",   "12-way -24V",     "T-DC-",    "Dc24VNegative", 4, 12, null, null, true),
            new SeedDeviceType(TerminalId, "Test",   "Terminal",        "T-TB",     "Terminal240", 1, 0, null, null, true),
            new SeedDeviceType(BridgeId,   "Test",   "Jumper bar",      "T-BAR",    "Accessory",   0, 0, null, null, true),
            new SeedDeviceType(EndStopId,  "Test",   "End stop",        "T-STOP",   "Accessory",   0, 0, null, null, true),
            new SeedDeviceType(CoverId,    "Shelly", "Test Dual Cover", "T-COVER",  "Cover",       4, 2, null, null, true),
            new SeedDeviceType(RgbwId,     "Shelly", "Test RGBWW",      "T-RGBWW",  "LedController", 4, 5, null, null, true),
            new SeedDeviceType(MeterId,    "Shelly", "Test 3EM",        "T-3EM",    "EnergyMeter", 6, 3, null, null, true),
            new SeedDeviceType(LanId,      "Shelly", "Test LAN Switch", "T-LAN",    "Network",     4, 5, null, null, true)
        ],
        Enclosures:
        [
            new SeedEnclosure(EnclosureId,     "Test", "Box 6x24", 6, 24, "IP30"),
            new SeedEnclosure(TinyEnclosureId, "Test", "Box 1x6",  1,  6, "IP30")
        ],
        RuleSets:
        [
            new SeedRuleSet(RuleSetId, "Test rules", 1, true, new RuleSetPayload(
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
                    Dc24VPositive: DcPosId,
                    Dc24VNegative: DcNegId,
                    ExternalDriver: [Psu240Id, Psu100Id]),
                Terminals: new TerminalRules(
                    DeviceTypeId: TerminalId,
                    BlocksPerCircuit: 1,
                    BridgeBarDeviceTypeId: BridgeId,
                    BridgeBarWays: 10,
                    EndStopDeviceTypeId: EndStopId,
                    EndStopsPerBank: 2)))
        ]);
}
