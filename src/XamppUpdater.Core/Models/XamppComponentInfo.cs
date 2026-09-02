namespace XamppUpdater.Core.Models;

public sealed record XamppComponentInfo(
    XamppComponentType Type,
    bool IsInstalled,
    string? Version,
    string ExecutablePath,
    string? ServiceName,
    string? Detail = null);
