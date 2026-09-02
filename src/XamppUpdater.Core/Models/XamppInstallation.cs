namespace XamppUpdater.Core.Models;

public sealed record XamppInstallation(
    string RootPath,
    string DiscoverySource,
    IReadOnlyList<XamppComponentInfo> Components);
