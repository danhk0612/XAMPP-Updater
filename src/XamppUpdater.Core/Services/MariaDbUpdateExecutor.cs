using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IMariaDbUpdateExecutor
{
    Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        MariaDbCredentials? credentials = null,
        CancellationToken cancellationToken = default);
}

public sealed class MariaDbUpdateExecutor : IMariaDbUpdateExecutor
{
    private readonly IWindowsServiceController _serviceController;
    private readonly IComponentVersionDetector _versionDetector;

    public MariaDbUpdateExecutor(IWindowsServiceController? serviceController = null, IComponentVersionDetector? versionDetector = null)
    {
        _serviceController = serviceController ?? new WindowsServiceController();
        _versionDetector = versionDetector ?? new ComponentVersionDetector();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        MariaDbCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        if (target.Type != XamppComponentType.MariaDb || package.Type != XamppComponentType.MariaDb || backup.Manifest.Type != XamppComponentType.MariaDb)
            throw new ArgumentException("MariaDB 업데이트에 필요한 대상/패키지/백업 정보가 아닙니다.");

        var component = installation.Components.First(item => item.Type == XamppComponentType.MariaDb);
        var currentVersion = component.Version ?? backup.Manifest.CurrentVersion;
        ValidateInputs(installation, target, package, backup, currentVersion);
        VerifyBackupIntegrity(backup);

        var steps = new List<string>();
        var warnings = new List<string>();
        var directMajorUpgrade = !IsSameSeries(currentVersion, target.Version);
        if (directMajorUpgrade)
        {
            warnings.Add($"MariaDB 직접 major 업그레이드 경로: {currentVersion} → {target.Version}. 기존 data 원본은 수정하지 않고 복사본에서만 새 서버 기동과 업그레이드 도구를 실행합니다.");
        }

        var xamppRoot = Path.GetFullPath(installation.RootPath);
        var mysqlRoot = Path.Combine(xamppRoot, "mysql");
        var serviceName = component.ServiceName;
        var unmanagedRunning = serviceName is null &&
            (Process.GetProcessesByName("mysqld").Length > 0 || Process.GetProcessesByName("mariadbd").Length > 0);
        if (unmanagedRunning) throw new InvalidOperationException("XAMPP MariaDB가 관리 가능한 Windows 서비스 없이 실행 중입니다. 먼저 종료해야 합니다.");
        if (serviceName is null) throw new InvalidOperationException("MariaDB 실제 업데이트에는 XAMPP mysql을 가리키는 Windows 서비스가 필요합니다.");

        var wasRunning = string.Equals(_serviceController.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var token = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(xamppRoot, $".xampp-updater-mariadb-stage-{token}");
        var extractRoot = Path.Combine(stageRoot, "package");
        var oldRoot = Path.Combine(xamppRoot, $".xampp-updater-mariadb-old-{token}");
        var swapped = false;
        var serviceStartedByUpdater = false;

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractRoot, overwriteFiles: true);
            var payloadRoot = ResolvePayloadRoot(extractRoot, package.PayloadEntry);
            ValidateStagedMariaDb(payloadRoot, target.Version);
            steps.Add($"MariaDB {target.Version} 패키지 스테이징 완료");

            if (wasRunning)
            {
                await Task.Run(() => _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                steps.Add($"MariaDB 서비스 중지: {serviceName}");
            }

            Directory.Move(mysqlRoot, oldRoot);
            Directory.Move(payloadRoot, mysqlRoot);
            swapped = true;
            steps.Add("MariaDB 바이너리 디렉터리 교체 완료");

            PreserveDataByCopy(oldRoot, mysqlRoot);
            steps.Add("기존 MariaDB data 복사 완료 (롤백 원본 유지)");
            PreserveConfiguration(oldRoot, mysqlRoot);
            steps.Add("기존 MariaDB my.ini/my.cnf 보존 완료");
            EnsureServiceExecutableCompatibility(component.ExecutablePath, mysqlRoot);
            steps.Add("기존 Windows 서비스 실행 파일 경로 호환성 확인 완료");

            try
            {
                await Task.Run(() => _serviceController.Start(serviceName, TimeSpan.FromSeconds(60)), cancellationToken);
                serviceStartedByUpdater = true;
            }
            catch (Exception startEx)
            {
                var tail = TryReadErrorLogTail(mysqlRoot);
                if (!string.IsNullOrWhiteSpace(tail)) warnings.Add("새 MariaDB 오류 로그 마지막 내용:" + Environment.NewLine + tail);
                throw new InvalidOperationException("새 MariaDB 서비스 시작 실패: " + startEx.Message, startEx);
            }
            steps.Add($"MariaDB 서비스 시작 및 RUNNING 확인: {serviceName}");

            var upgrade = FindUpgradeTool(mysqlRoot);
            if (upgrade is not null)
            {
                var upgradeResult = await RunUpgradeAsync(upgrade, mysqlRoot, credentials, cancellationToken);
                if (upgradeResult.ExitCode != 0)
                    throw new InvalidOperationException("mariadb-upgrade/mysql_upgrade 실패: " + Compact(upgradeResult.Output));
                steps.Add($"업그레이드 도구 실행 완료: {Path.GetFileName(upgrade)}");
            }
            else
            {
                warnings.Add("mariadb-upgrade/mysql_upgrade 실행 파일을 찾지 못해 업그레이드 도구 단계는 생략했습니다.");
            }

            if (!string.Equals(_serviceController.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("업그레이드 후 MariaDB 서비스가 RUNNING 상태가 아닙니다.");
            steps.Add("업그레이드 후 MariaDB 서비스 RUNNING 재확인");

            var installedExecutable = ResolveInstalledServerExecutable(component.ExecutablePath, mysqlRoot);
            var detected = _versionDetector.Detect(XamppComponentType.MariaDb, installedExecutable);
            if (!string.Equals(detected.Version, target.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"교체 후 MariaDB 버전 검증 실패: 기대 {target.Version}, 실제 {detected.Version ?? "확인 실패"} / {Compact(detected.Output)}");
            steps.Add("교체 후 MariaDB 버전 검증 완료");

            if (!wasRunning)
            {
                await Task.Run(() => _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                serviceStartedByUpdater = false;
                steps.Add("업데이트 전 STOPPED 상태 복원");
            }

            TryDeleteDirectory(oldRoot);
            TryDeleteDirectory(stageRoot);
            return new UpdateExecutionResult(true, false, currentVersion, target.Version, steps, warnings);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            try
            {
                if (string.Equals(_serviceController.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                    _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30));
            }
            catch (Exception stopEx) { rollbackErrors.Add("롤백 전 MariaDB 중지 실패: " + stopEx.Message); }

            if (swapped)
            {
                try
                {
                    TryDeleteDirectory(mysqlRoot);
                    if (!Directory.Exists(oldRoot)) throw new DirectoryNotFoundException("롤백 원본 MariaDB 디렉터리가 없습니다: " + oldRoot);
                    Directory.Move(oldRoot, mysqlRoot);
                    steps.Add("MariaDB 디렉터리 자동 롤백 완료");
                }
                catch (Exception rollbackEx) { rollbackErrors.Add("MariaDB 디렉터리 롤백 실패: " + rollbackEx.Message); }
            }

            if (wasRunning)
            {
                try
                {
                    _serviceController.Start(serviceName, TimeSpan.FromSeconds(60));
                    serviceStartedByUpdater = false;
                    steps.Add("MariaDB 서비스 원상복구 완료");
                }
                catch (Exception startEx)
                {
                    rollbackErrors.Add("MariaDB 서비스 원상복구 실패: " + startEx.Message);
                    var tail = TryReadErrorLogTail(mysqlRoot);
                    if (!string.IsNullOrWhiteSpace(tail)) rollbackErrors.Add("롤백 MariaDB 오류 로그 마지막 내용:" + Environment.NewLine + tail);
                }
            }

            warnings.AddRange(rollbackErrors);
            TryDeleteDirectory(stageRoot);
            return new UpdateExecutionResult(false, swapped, currentVersion, target.Version, steps, warnings, ex.Message);
        }
        finally
        {
            if (!wasRunning && serviceStartedByUpdater)
            {
                try { _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30)); } catch { }
            }
        }
    }

    private static async Task<ProcessResult> RunUpgradeAsync(
        string executable,
        string workingDirectory,
        MariaDbCredentials? credentials,
        CancellationToken cancellationToken)
    {
        string? optionFile = null;
        try
        {
            var arguments = new List<string>();
            if (credentials is not null)
            {
                optionFile = Path.Combine(Path.GetTempPath(), $"xampp-updater-mariadb-upgrade-{Guid.NewGuid():N}.cnf");
                var text = "[client]" + Environment.NewLine +
                           $"user=\"{MariaDbLogicalBackupService.EscapeOptionFileValue(credentials.UserName)}\"" + Environment.NewLine +
                           $"password=\"{MariaDbLogicalBackupService.EscapeOptionFileValue(credentials.Password)}\"" + Environment.NewLine;
                await File.WriteAllTextAsync(optionFile, text, cancellationToken);
                arguments.Add($"--defaults-extra-file={optionFile}");
            }

            var result = await RunWithTimeoutAsync(executable, arguments, workingDirectory, TimeSpan.FromMinutes(3), cancellationToken);
            if (result.ExitCode == 0 || credentials is not null || !LooksLikeAuthenticationFailure(result.Output))
                return result;

            return await RunWithTimeoutAsync(executable, new[] { "--user=root" }, workingDirectory, TimeSpan.FromMinutes(3), cancellationToken);
        }
        finally
        {
            if (optionFile is not null && File.Exists(optionFile))
            {
                try { File.Delete(optionFile); } catch { }
            }
        }
    }

    internal static bool IsSameSeries(string currentVersion, string targetVersion) =>
        Version.TryParse(currentVersion, out var current) && Version.TryParse(targetVersion, out var target) &&
        current.Major == target.Major && current.Minor == target.Minor;

    private static void ValidateInputs(XamppInstallation installation, UpdateTargetOption target, PackagePreparationResult package, BackupResult backup, string currentVersion)
    {
        var expectedRoot = Path.Combine(Path.GetFullPath(installation.RootPath), "mysql");
        if (!string.Equals(target.Version, package.Version, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("선택 버전과 준비된 MariaDB 패키지 버전이 다릅니다.");
        if (!string.Equals(target.Version, backup.Manifest.TargetVersion, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("선택 버전과 MariaDB 백업 manifest의 대상 버전이 다릅니다. 백업을 다시 생성하세요.");
        if (!string.Equals(currentVersion, backup.Manifest.CurrentVersion, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("현재 MariaDB 버전이 백업 생성 시점과 다릅니다. 백업을 다시 생성하세요.");
        if (!PathsEqual(installation.RootPath, backup.Manifest.XamppRoot) || !PathsEqual(expectedRoot, backup.Manifest.ComponentRoot)) throw new InvalidOperationException("백업이 현재 선택한 XAMPP의 mysql 디렉터리에 대한 것이 아닙니다.");
        if (!File.Exists(package.PackagePath)) throw new FileNotFoundException("준비된 MariaDB 패키지를 찾을 수 없습니다.", package.PackagePath);
        if (!File.Exists(backup.ManifestPath)) throw new FileNotFoundException("MariaDB 롤백 manifest를 찾을 수 없습니다.", backup.ManifestPath);
        if (backup.Manifest.LogicalBackup is null) throw new InvalidOperationException("MariaDB 실제 업데이트에는 전체 논리 백업 SQL이 포함된 안전 백업이 필요합니다. 백업 생성을 다시 실행하세요.");
    }

    private static void VerifyBackupIntegrity(BackupResult backup)
    {
        var filesRoot = Path.Combine(backup.Manifest.BackupRoot, "files");
        if (!Directory.Exists(filesRoot)) throw new DirectoryNotFoundException("MariaDB 물리 백업 files 디렉터리가 없습니다: " + filesRoot);
        foreach (var item in backup.Manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(filesRoot, item.RelativePath));
            if (!IsUnderRoot(path, filesRoot) || !File.Exists(path)) throw new InvalidDataException("MariaDB 물리 백업 파일이 누락되었습니다: " + item.RelativePath);
            if (new FileInfo(path).Length != item.Size || !string.Equals(ComputeSha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MariaDB 물리 백업 무결성 검증 실패: " + item.RelativePath);
        }
        var logical = backup.Manifest.LogicalBackup!;
        var logicalPath = Path.GetFullPath(Path.Combine(backup.Manifest.BackupRoot, logical.RelativePath));
        if (!IsUnderRoot(logicalPath, backup.Manifest.BackupRoot) || !File.Exists(logicalPath) ||
            new FileInfo(logicalPath).Length != logical.Size || !string.Equals(ComputeSha256(logicalPath), logical.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MariaDB 논리 백업 SQL의 크기/SHA256 검증에 실패했습니다.");
    }

    private static string ResolvePayloadRoot(string extractRoot, string payloadEntry)
    {
        var serverPath = Path.Combine(extractRoot, payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(serverPath)) throw new InvalidDataException("압축 해제 후 MariaDB 서버 실행 파일을 찾을 수 없습니다.");
        var bin = Path.GetDirectoryName(serverPath) ?? throw new InvalidDataException("MariaDB bin 경로를 확인할 수 없습니다.");
        return Directory.GetParent(bin)?.FullName ?? throw new InvalidDataException("MariaDB 패키지 루트를 확인할 수 없습니다.");
    }

    private static void ValidateStagedMariaDb(string root, string targetVersion)
    {
        var executable = FindServerExecutable(root) ?? throw new FileNotFoundException("스테이징 MariaDB에서 mariadbd.exe/mysqld.exe를 찾을 수 없습니다.");
        var result = RunWithTimeoutAsync(executable, new[] { "--version" }, root, TimeSpan.FromSeconds(15), CancellationToken.None).GetAwaiter().GetResult();
        var parsed = ComponentVersionDetector.ParseVersion(XamppComponentType.MariaDb, result.Output);
        if (result.ExitCode != 0 || !string.Equals(parsed, targetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"스테이징 MariaDB 버전 검증 실패: 기대 {targetVersion}, 실제 {parsed ?? "확인 실패"} / {Compact(result.Output)}");
    }

    private static void PreserveDataByCopy(string oldRoot, string newRoot)
    {
        var source = Path.Combine(oldRoot, "data");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("기존 MariaDB data 디렉터리를 찾을 수 없습니다: " + source);
        var destination = Path.Combine(newRoot, "data");
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        CopyDirectory(source, destination);
    }

    private static void PreserveConfiguration(string oldRoot, string newRoot)
    {
        foreach (var relative in new[] { Path.Combine("bin", "my.ini"), "my.ini", Path.Combine("bin", "my.cnf"), "my.cnf" })
        {
            var source = Path.Combine(oldRoot, relative);
            if (!File.Exists(source)) continue;
            var destination = Path.Combine(newRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void EnsureServiceExecutableCompatibility(string configuredExecutable, string liveRoot)
    {
        var relative = Path.GetRelativePath(liveRoot, configuredExecutable);
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == "..")
            throw new InvalidOperationException("MariaDB 서비스 실행 파일이 선택한 XAMPP mysql 디렉터리 밖을 가리킵니다: " + configuredExecutable);
        var destination = Path.Combine(liveRoot, relative);
        if (File.Exists(destination)) return;
        var packagedServer = FindServerExecutable(liveRoot) ?? throw new FileNotFoundException("새 MariaDB 패키지에서 서비스용 서버 실행 파일을 찾을 수 없습니다.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(packagedServer, destination, overwrite: true);
    }

    private static string ResolveInstalledServerExecutable(string configuredExecutable, string root)
    {
        var configured = Path.Combine(root, "bin", Path.GetFileName(configuredExecutable));
        if (File.Exists(configured)) return configured;
        return FindServerExecutable(root) ?? throw new FileNotFoundException("업데이트된 MariaDB 서버 실행 파일을 찾을 수 없습니다.");
    }

    private static string? FindServerExecutable(string root)
    {
        var mariadbd = Path.Combine(root, "bin", "mariadbd.exe");
        if (File.Exists(mariadbd)) return mariadbd;
        var mysqld = Path.Combine(root, "bin", "mysqld.exe");
        return File.Exists(mysqld) ? mysqld : null;
    }

    private static string? FindUpgradeTool(string root)
    {
        foreach (var name in new[] { "mariadb-upgrade.exe", "mysql_upgrade.exe" })
        {
            var path = Path.Combine(root, "bin", name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static bool LooksLikeAuthenticationFailure(string output) =>
        output.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("using password", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("ERROR 1045", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("authentication", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadErrorLogTail(string root)
    {
        try
        {
            var data = Path.Combine(root, "data");
            if (!Directory.Exists(data)) return null;
            var path = Directory.EnumerateFiles(data, "*.err", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(data, "mysql_error.log", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return path is null ? null : string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(40));
        }
        catch { return null; }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task<ProcessResult> RunWithTimeoutAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        var bin = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(bin))
        {
            var path = start.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            start.Environment["PATH"] = string.IsNullOrWhiteSpace(path) ? bin : bin + Path.PathSeparator + path;
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"명령 실행 시간 초과 ({timeout.TotalSeconds:N0}초): {Path.GetFileName(executable)}");
        }
        var output = string.Join(Environment.NewLine, new[] { await stdout, await stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static string Compact(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();
    private sealed record ProcessResult(int ExitCode, string Output);
}
