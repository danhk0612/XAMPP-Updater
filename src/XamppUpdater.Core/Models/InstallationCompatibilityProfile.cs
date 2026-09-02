namespace XamppUpdater.Core.Models;

public sealed record InstallationCompatibilityProfile(
    string RootPath,
    BinaryArchitecture ApacheArchitecture,
    BinaryArchitecture PhpArchitecture,
    BinaryArchitecture MariaDbArchitecture,
    PhpRuntimeProfile Php,
    ApachePhpIntegration ApachePhpIntegration,
    string? MariaDbSeries);

public sealed record PhpRuntimeProfile(
    bool? ThreadSafe,
    string? Compiler,
    string? ExtensionBuild,
    string? ApiVersion);

public sealed record ApachePhpIntegration(
    bool IsModuleLoaded,
    string? ModuleName,
    string? ModulePath,
    string? ConfigPath);

public enum BinaryArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64
}
