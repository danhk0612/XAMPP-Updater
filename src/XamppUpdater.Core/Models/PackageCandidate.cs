namespace XamppUpdater.Core.Models;

public sealed record CandidatePackageCatalog(
    DateTimeOffset CheckedAt,
    IReadOnlyList<PackageCandidate> Candidates);

public sealed record PackageCandidate(
    XamppComponentType Type,
    string? Version,
    string? FileName,
    string? DownloadUrl,
    BinaryArchitecture Architecture,
    string? Compiler,
    bool? ThreadSafe,
    string? Sha256,
    string? VerificationUrl,
    CandidateCompatibilityStatus Status,
    string Reason);

public enum CandidateCompatibilityStatus
{
    Automatic,
    Assisted,
    ManualReview,
    Unavailable
}
