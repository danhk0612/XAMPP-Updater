namespace XamppUpdater.Core.Services;

public static class UpdateProgressStages
{
    public const string BackupVerify = "BackupVerify";
    public const string BeforeSnapshot = "BeforeSnapshot";
    public const string Execute = "Execute";
    public const string AfterSnapshot = "AfterSnapshot";
    public const string Rollback = "Rollback";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
}
