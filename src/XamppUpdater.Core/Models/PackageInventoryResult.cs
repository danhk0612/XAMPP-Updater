namespace XamppUpdater.Core.Models;

public sealed record PackageInventoryResult(
    XamppComponentType Type,
    int CurrentFiles,
    int PackageFiles,
    int CommonFiles,
    int CurrentOnlyFiles,
    int PackageOnlyFiles,
    IReadOnlyList<string> CompatibilityItems);
