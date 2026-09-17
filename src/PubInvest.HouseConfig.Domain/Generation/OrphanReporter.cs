using PubInvest.HouseConfig.Domain.Diagnostics;
using PubInvest.HouseConfig.Domain.Layout;

namespace PubInvest.HouseConfig.Domain.Generation;

/// A device as it was stored before re-generation, with the circuits it carried.
public sealed record ExistingAssignment(
    Guid DeviceId,
    string Label,
    IReadOnlyList<ChannelAssignment> Channels);

/// Circuit names and rooms live on the submain, so they survive re-generation
/// untouched. The one thing that must not happen silently is a circuit that was
/// wired to a channel losing its home, which is what this reports.
public static class OrphanReporter
{
    public static IReadOnlyList<Diagnostic> Report(
        PanelLayout newLayout,
        IReadOnlyList<ExistingAssignment> existing)
    {
        if (existing.Count == 0) return [];

        var stillAssigned = newLayout.Devices
            .SelectMany(d => d.Channels)
            .Select(c => c.CircuitId)
            .OfType<Guid>()
            .ToHashSet();

        return existing
            .SelectMany(d => d.Channels)
            .Select(c => c.CircuitId)
            .OfType<Guid>()
            .Distinct()
            .Where(id => !stillAssigned.Contains(id))
            .OrderBy(id => id)
            .Select(id => new Diagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.OrphanedAssignment,
                $"Circuit {id} was wired to a channel but has no channel in the new layout.",
                "Re-add the circuit to the submain, or delete it if it is no longer needed."))
            .ToList();
    }
}
