using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Circuits;

namespace PubInvest.HouseConfig.Api.Documents;

/// The words a person reads for an enum, on paper. These are the same words the
/// UI uses (ui/src/panel/deviceStyle.ts and the wizard), so the screen and the
/// issued PDF never call one thing by two names. A category or circuit type with
/// no entry here falls back to its enum name rather than failing the print, and
/// a test checks every value has one.
public static class Wording
{
    private static readonly Dictionary<DeviceCategory, string> Kinds = new()
    {
        [DeviceCategory.Terminal240] = "Terminal",
        [DeviceCategory.Isolator] = "Isolator",
        [DeviceCategory.Dimmer240] = "Dimmer",
        [DeviceCategory.Dimmer0_10V] = "Tape dimmer",
        [DeviceCategory.Relay] = "Relay",
        // Shelly calls the part a Cover; the device label keeps that word because
        // stored position overrides are keyed on it. What it does is drive blinds.
        [DeviceCategory.Cover] = "Blinds",
        [DeviceCategory.LedController] = "LED controller",
        [DeviceCategory.EnergyMeter] = "Energy meter",
        [DeviceCategory.Network] = "Network",
        [DeviceCategory.Dc24VPositive] = "+24V",
        [DeviceCategory.Dc24VNegative] = "-24V",
        [DeviceCategory.ExternalDriver] = "Driver (external)",
        [DeviceCategory.Accessory] = "Accessory",
    };

    private static readonly Dictionary<CircuitType, string> CircuitTypes = new()
    {
        [Domain.Circuits.CircuitType.DimmedLighting] = "Dimmed lighting",
        [Domain.Circuits.CircuitType.Switched] = "Switched",
        [Domain.Circuits.CircuitType.LedTape] = "LED tape",
        [Domain.Circuits.CircuitType.Cover] = "Blinds",
        [Domain.Circuits.CircuitType.RgbwTape] = "Colour LED tape",
    };

    public static string Kind(DeviceCategory category)
        => Kinds.TryGetValue(category, out var kind) ? kind : category.ToString();

    public static bool HasKind(DeviceCategory category) => Kinds.ContainsKey(category);

    /// Circuit types are stored as strings; one that no longer parses is printed as it is.
    public static string CircuitType(string type)
        => Enum.TryParse<Domain.Circuits.CircuitType>(type, out var parsed) && CircuitTypes.TryGetValue(parsed, out var name)
            ? name
            : type;

    public static bool HasCircuitType(Domain.Circuits.CircuitType type) => CircuitTypes.ContainsKey(type);
}
