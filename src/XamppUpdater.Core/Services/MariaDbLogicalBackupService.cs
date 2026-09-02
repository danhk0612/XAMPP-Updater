using System.Diagnostics;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IMariaDbLogicalBackupService
{
    Task<MariaDbLogicalBackupResult> CreateAsync(
        UpdatePreflightReport preflight,
        MariaDbCredentials? credentials = null,
        CancellationToken cancellationToken = default);
}

public sealed class MariaDbLogicalBackupService : IMariaDbLogicalBackupService
{
    public async Task<MariaDbLogicalBackupResult> CreateAsync(
        UpdatePreflightReport preflight,
        MariaDbCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        if (preflight.Type != XamppComponentType.MariaDb)
        {
            throw new ArgumentException("MariaDB 준비 점검 결과가 필요합니다.", nameof(preflight));
        }

        var dumpExecutable = FindDumpExecutable(preflight.ComponentRoot)
            ?? throw new FileNotFoundException("mariadb-dump.exe 또는 mysqldump.exe를 찾을 수 없습니다.");

        if (credentials is null)
        {
            var currentDefaults = await RunDumpAsync(preflight, dumpExecutable, null, null, cancellationToken);
            if (currentDefaults.Success)
            {
                return currentDefaults;
            }

            if (!IsAuthenticationFailure(currentDefaults.ErrorText))
            {
                return currentDefaults;
            }

            var rootNoPassword = await RunDumpAsync(preflight, dumpExecutable, "root", string.Empty, cancellationToken);
            if (rootNoPassword.Success)
            {
                return rootNoPassword;
            }

            if (IsAuthenticationFailure(rootNoPassword.ErrorText))
            {
                return rootNoPassword with { AuthenticationRequired = true };
            }

            return rootNoPassword;
        }

        return await RunDumpAsync(
            preflight,
            dumpExecutable,
            credentials.UserName,
            credentials.Password,
            cancellationToken);
    }

    internal static bool IsAuthenticationFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("using password", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ERROR 1045", StringComparison.OrdinalIgnoreCase);
    }

    internal static string EscapeOptionFileValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static async Task<MariaDbLogicalBackupResult> RunDumpAsync(
        UpdatePreflightReport preflight,
        string dumpExecutable,
        string? userName,
        string? password,
        CancellationToken cancellationToken)
    {
        var logicalRoot = Path.Combine(preflight.BackupDestination, "logical");
        Directory.CreateDirectory(logicalRoot);
        var outputPath = Path.Combine(logicalRoot, "all-databases.sql");
        var temporaryOutput = Path.Combine(logicalRoot, $"all-databases-{Guid.NewGuid():N}.sql.part");
        var optionFile = default(string);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dumpExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(dumpExecutable) ?? preflight.ComponentRoot
            };

            if (userName is not null)
            {
                optionFile = Path.Combine(Path.GetTempPath(), $"xampp-updater-mariadb-{Guid.NewGuid():N}.cnf");
                var optionText = "[client]" + Environment.NewLine +
                                 $"user=\"{EscapeOptionFileValue(userName)}\"" + Environment.NewLine +
                                 $"password=\"{EscapeOptionFileValue(password ?? string.Empty)}\"" + Environment.NewLine;
                await File.WriteAllTextAsync(optionFile, optionText, cancellationToken);
                startInfo.ArgumentList.Add($"--defaults-extra-file={optionFile}");
            }

            foreach (var argument in new[]
                     {
                         "--all-databases",
                         "--routines",
                         "--events",
                         "--triggers",
                         "--hex-blob",
                         "--single-transaction",
                         "--quick",
                         "--add-drop-database",
                         "--default-character-set=utf8mb4"
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            string errorText;
            await using (var output = new FileStream(
                             temporaryOutput,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);
                await copyTask;
                await output.FlushAsync(cancellationToken);
                errorText = await errorTask;
            }

            if (process.ExitCode != 0)
            {
                if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
                return new MariaDbLogicalBackupResult(false, false, null, 0, null, errorText.Trim());
            }

            File.Move(temporaryOutput, outputPath, overwrite: true);
            var info = new FileInfo(outputPath);
            var sha256 = ComputeSha256(outputPath);
            return new MariaDbLogicalBackupResult(true, false, outputPath, info.Length, sha256, errorText.Trim());
        }
        finally
        {
            if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
            if (optionFile is not null && File.Exists(optionFile)) File.Delete(optionFile);
        }
    }

    private static string? FindDumpExecutable(string componentRoot)
    {
        foreach (var fileName in new[] { "mariadb-dump.exe", "mysqldump.exe" })
        {
            var path = Path.Combine(componentRoot, "bin", fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed record MariaDbCredentials(string UserName, string Password);

public sealed record MariaDbLogicalBackupResult(
    bool Success,
    bool AuthenticationRequired,
    string? FilePath,
    long Size,
    string? Sha256,
    string ErrorText);
