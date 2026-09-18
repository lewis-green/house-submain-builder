namespace PubInvest.HouseConfig.Domain.Generation;

/// A position the engineer chose by dragging, keyed by the device's generated
/// label rather than its database id: ids are re-issued on every re-generation,
/// labels are deterministic.
public sealed record PositionOverride(string Label, int RowIndex, int StartSlot);
