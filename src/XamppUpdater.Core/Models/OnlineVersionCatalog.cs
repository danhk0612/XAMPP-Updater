namespace XamppUpdater.Core.Models;

public sealed record OnlineVersionCatalog(
    DateTimeOffset CheckedAt,
    IReadOnlyList<OnlineComponentVersion> Components);

public sealed record OnlineComponentVersion(
    XamppComponentType Type,
    string? UpstreamLatestVersion,
    string? XamppBundledVersion,
    string UpstreamSource,
    string CompatibilityNote);
