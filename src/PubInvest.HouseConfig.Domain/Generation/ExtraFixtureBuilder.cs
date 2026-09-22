using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;

namespace PubInvest.HouseConfig.Domain.Generation;

/// A device asked for by hand rather than derived from the circuits.
public sealed record ExtraFixture(Guid DeviceTypeId, int Quantity);

public sealed record ExtraFixtures(
    IReadOnlyList<RequiredDevice> Devices,
    IReadOnlyList<Diagnostic> Diagnostics);

/// Panel gear that feeds nothing: energy meters, a LAN switch, a spare relay
/// someone wants on the rail. Nothing in the circuit list implies these, so they
/// are asked for by device type and quantity.
///
/// They are built last so their labels can continue the numbering the generated
/// devices started: a hand-added relay alongside two generated ones is "Relay 3",
/// not a second "Relay 1" — labels are what position overrides are keyed on.
public static class ExtraFixtureBuilder
{
    public static ExtraFixtures Build(
        IReadOnlyList<ExtraFixture> fixtures,
        IReadOnlyList<RequiredDevice> alreadyBuilt,
        DeviceCatalogue catalogue)
    {
        if (fixtures.Count == 0) return new ExtraFixtures([], []);

        var devices = new List<RequiredDevice>();
        var diagnostics = new List<Diagnostic>();

        var used = alreadyBuilt
            .GroupBy(d => d.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var fixture in fixtures)
        {
            if (fixture.Quantity <= 0) continue;

            var deviceType = catalogue.FindActive(fixture.DeviceTypeId);

            if (deviceType is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for an extra fixture (id {fixture.DeviceTypeId}).",
                    "Remove it from the submain, or re-activate the catalogue entry."));
                continue;
            }

            for (var n = 0; n < fixture.Quantity; n++)
            {
                var index = used.GetValueOrDefault(deviceType.Category) + 1;
                used[deviceType.Category] = index;

                devices.Add(new RequiredDevice(
                    deviceType.Id,
                    deviceType.Category,
                    deviceType.ModuleWidth,
                    $"{LabelStem(deviceType.Category)} {index}",
                    // Channels here are outputs of the device, not circuits
                    // anyone names: a meter measures and a switch has ports.
                    []));
            }
        }

        return new ExtraFixtures(devices, diagnostics);
    }

    private static string LabelStem(DeviceCategory category) => category switch
    {
        DeviceCategory.Dimmer240 => "Dimmer",
        DeviceCategory.Dimmer0_10V => "Tape Dimmer",
        DeviceCategory.Relay => "Relay",
        DeviceCategory.Cover => "Cover",
        DeviceCategory.LedController => "LED",
        DeviceCategory.EnergyMeter => "Meter",
        DeviceCategory.Network => "LAN",
        DeviceCategory.Isolator => "Isolator",
        DeviceCategory.Terminal240 => "Terminal",
        _ => category.ToString(),
    };
}
