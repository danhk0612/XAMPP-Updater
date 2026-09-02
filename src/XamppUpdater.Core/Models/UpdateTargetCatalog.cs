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
    bool PackageResolved)
{
    public string DisplayText => $"{Version} — {Label}{(PackageResolved ? "" : " (패키지 확인 필요)")}";
}

public enum UpdateTargetSource
{
    Installed,
    SameSeriesCandidate,
    XamppBundle,
    UpstreamLatest
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
