namespace XamppUpdater.Core.Models;

public sealed record ConfigDiffResult(
    XamppComponentType Type,
    string Baseline,
    IReadOnlyList<ConfigDiffItem> Items,
    IReadOnlyList<string> Warnings)
{
    public int Changed => Items.Count(item => item.Kind == ConfigDiffKind.Changed);
    public int CurrentOnly => Items.Count(item => item.Kind == ConfigDiffKind.CurrentOnly);
    public int TargetOnly => Items.Count(item => item.Kind == ConfigDiffKind.TargetOnly);
    public int Same => Items.Count(item => item.Kind == ConfigDiffKind.Same);
}

public sealed record ConfigDiffItem(
    string Key,
    ConfigDiffKind Kind,
    string? CurrentValue = null,
    string? TargetValue = null);

public enum ConfigDiffKind
{
    Same,
    Changed,
    CurrentOnly,
    TargetOnly
}
