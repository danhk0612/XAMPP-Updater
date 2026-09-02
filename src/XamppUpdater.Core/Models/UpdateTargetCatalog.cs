namespace XamppUpdater.Core.Models;

public sealed record UpdateTargetCatalog(
    IReadOnlyList<UpdateTargetOption> Apache,
    IReadOnlyList<UpdateTargetOption> Php,
    IReadOnlyList<UpdateTargetOption> MariaDb);

public sealed record UpdateTargetOption(
    XamppComponentType Type,
    string Version,
    string Label,
    UpdateTargetSource Source,
    bool IsLatest,
    bool PackageResolved,
    string? PackageUrl = null,
    string? PackageFileName = null,
    bool IsEol = false)
{
    public string DisplayText => $"{Version} — {Label}{(IsEol ? " [EOL]" : string.Empty)}";
}

public enum UpdateTargetSource
{
    Installed,
    SameSeriesCandidate,
    XamppBundle,
    UpstreamLatest,
    OfficialArchive
}

public sealed record UpdatePlan(
    XamppComponentType Type,
    string CurrentVersion,
    string TargetVersion,
    CandidateCompatibilityStatus Status,
    IReadOnlyList<UpdatePlanStep> Steps,
    string Summary);

public sealed record UpdatePlanStep(
    UpdatePlanStepKind Kind,
    string Title,
    string Detail);

public enum UpdatePlanStepKind
{
    Automatic,
    Assisted,
    UserConfirmation
}
