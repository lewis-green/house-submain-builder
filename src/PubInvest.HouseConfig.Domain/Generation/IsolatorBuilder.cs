using PubInvest.HouseConfig.Domain.Catalogue;
using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Rules;

namespace PubInvest.HouseConfig.Domain.Generation;

/// Every panel starts with one two-pole isolator; the incoming submain feed
/// lands on it rather than on a terminal block.
public static class IsolatorBuilder
{
    public static (RequiredDevice? Device, IReadOnlyList<Diagnostic> Diagnostics) Build(
        RuleSetPayload rules,
        DeviceCatalogue catalogue)
    {
        var deviceType = catalogue.FindActive(rules.PreferredDevice.Isolator);

        if (deviceType is null)
        {
            // A panel with no means of isolation is not one you would install, so
            // this is an error rather than a warning.
            return (null, [
                new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.NoPreferredDevice,
                    $"No active catalogue device for the main isolator (id {rules.PreferredDevice.Isolator}).",
                    "Choose an active two-pole isolator in the ruleset.")
            ]);
        }

        return (new RequiredDevice(
            deviceType.Id,
            deviceType.Category,
            deviceType.ModuleWidth,
            "Isolator",
            []), []);
    }
}
