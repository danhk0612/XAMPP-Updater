using System.Globalization;

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
    public string DisplayText => $"{Version} — {DisplayLabel}{(IsEol ? " [EOL]" : string.Empty)}";

    private string DisplayLabel
    {
        get
        {
            if (!CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                return Label;

            return Label
                .Replace("현재 버전 / 업데이트 없음", "Current version / no update", StringComparison.Ordinal)
                .Replace("현재 계열 추천", "Recommended for current series", StringComparison.Ordinal)
                .Replace("XAMPP 공식 기준", "XAMPP official baseline", StringComparison.Ordinal)
                .Replace("업데이트 없음", "No update", StringComparison.Ordinal)
                .Replace("현재 버전", "Current version", StringComparison.Ordinal)
                .Replace("최신", "Latest", StringComparison.Ordinal)
                .Replace("계열", "series", StringComparison.Ordinal);
        }
    }
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
