namespace XamppUpdater.Core.Models;

public sealed record SelectableVersionCatalog(
    DateTimeOffset CheckedAt,
    IReadOnlyList<SelectableVersionEntry> Entries);

public sealed record SelectableVersionEntry(
    XamppComponentType Type,
    string Version,
    string SourceLabel,
    string? PackageUrl,
    string? PackageFileName,
    bool IsEol = false);
