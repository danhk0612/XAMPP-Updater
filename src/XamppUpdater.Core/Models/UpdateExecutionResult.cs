namespace XamppUpdater.Core.Models;

public sealed record UpdateExecutionResult(
    bool Success,
    bool RolledBack,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Warnings,
    string? Error = null);
