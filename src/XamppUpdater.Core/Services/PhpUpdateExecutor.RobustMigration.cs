namespace XamppUpdater.Core.Services;

public sealed partial class PhpUpdateExecutor
{
    public PhpUpdateExecutor()
        : this(null, new RobustPhpIniMigrationService(), null)
    {
    }
}
