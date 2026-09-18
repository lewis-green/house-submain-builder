namespace PubInvest.HouseConfig.Domain.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Suggestion = null);

public static class DiagnosticCodes
{
    public const string EnclosureTooSmall = "ENCLOSURE_TOO_SMALL";
    public const string NoPreferredDevice = "NO_PREFERRED_DEVICE";
    public const string PsuUnsized = "PSU_UNSIZED";
    public const string OrphanedAssignment = "ORPHANED_ASSIGNMENT";
    public const string TapeLoadMissing = "TAPE_LOAD_MISSING";
    public const string DeviceWiderThanRow = "DEVICE_WIDER_THAN_ROW";
    public const string PositionOverrideDropped = "POSITION_OVERRIDE_DROPPED";
}
