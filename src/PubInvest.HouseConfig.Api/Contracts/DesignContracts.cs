namespace PubInvest.HouseConfig.Api.Contracts;

public sealed record PreviewRequest(
    Guid? EnclosureTypeId,
    Guid? RuleSetId,
    IReadOnlyList<CircuitRequest>? Circuits);

public sealed record ChannelResponse(
    int ChannelIndex, Guid? CircuitId, string? CircuitName, string? CircuitRoom, bool IsSpare);

public sealed record PlacedDeviceResponse(
    Guid? Id,
    Guid DeviceTypeId,
    string Category,
    int RowIndex,
    int StartSlot,
    int ModuleWidth,
    string Label,
    string TerminalRole,
    IReadOnlyList<ChannelResponse> Channels);

public sealed record LayoutResponse(int Rows, int SlotsPerRow, IReadOnlyList<PlacedDeviceResponse> Devices);

public sealed record DiagnosticResponse(string Severity, string Code, string Message, string? Suggestion);

public sealed record BomLineResponse(
    Guid CatalogueId, string PartNumber, string Description, int Quantity, bool PanelMounted);

public sealed record DesignSummary(
    int RowsUsed, int SlotsUsed, int TotalSlots, int DeviceCount, int SpareChannels);

public sealed record DesignResponse(
    Guid SubmainId,
    int LayoutVersion,
    LayoutResponse Layout,
    IReadOnlyList<DiagnosticResponse> Diagnostics,
    IReadOnlyList<BomLineResponse> Bom,
    DesignSummary Summary);
