namespace XamppUpdater.Core.Models;

public sealed record BackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    XamppComponentType Type,
    string XamppRoot,
    string ComponentRoot,
    string CurrentVersion,
    string TargetVersion,
    string BackupRoot,
    string? ServiceName,
    string? ServiceState,
    bool ProcessWasRunning,
    IReadOnlyList<BackupManifestFile> Files);

public sealed record BackupManifestFile(
    string RelativePath,
    long Size,
    string Sha256);

public sealed record BackupResult(
    BackupManifest Manifest,
    string ManifestPath,
    long CopiedBytes,
    int CopiedFiles);
