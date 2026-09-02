using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class MariaDbLogicalBackupSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (!MariaDbLogicalBackupService.IsAuthenticationFailure("ERROR 1045 (28000): Access denied for user 'root'@'localhost'"))
        {
            throw new InvalidOperationException("MariaDB authentication failure detection smoke test failed.");
        }

        if (MariaDbLogicalBackupService.IsAuthenticationFailure("Unknown option --example"))
        {
            throw new InvalidOperationException("MariaDB non-authentication error detection smoke test failed.");
        }

        var escaped = MariaDbLogicalBackupService.EscapeOptionFileValue("a\\b\"c");
        if (!string.Equals(escaped, "a\\\\b\\\"c", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"MariaDB option-file escaping smoke test failed: {escaped}");
        }
    }
}
