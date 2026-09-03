using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class ConfigSnapshotRestoreService
{
    public async Task<ConfigSnapshotRestoreResult> RestoreSelectedAsync(
        XamppInstallation installation,
        ConfigSnapshotManifest snapshot,
        IReadOnlyCollection<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        if (relativePaths.Count == 0)
            throw new ArgumentException("선택된 설정 파일이 없습니다.", nameof(relativePaths));

        var selected = relativePaths
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("선택된 설정 파일이 없습니다.", nameof(relativePaths));

        var steps = new List<string>();
        ConfigSnapshotManifest? safety = null;
        ConfigSnapshotManifest? afterRestore = null;
        var serviceName = ResolveServiceName(installation, snapshot.Type);
        var serviceWasRunning = false;
        var serviceStopped = false;

        try
        {
            ValidateSnapshot(installation, snapshot);
            var componentRoot = GetComponentRoot(installation.RootPath, snapshot.Type);
            var managed = EnumerateManagedConfigFiles(componentRoot, snapshot.Type)
                .Select(path => Path.GetRelativePath(componentRoot, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var snapshotPaths = snapshot.Files.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var path in selected)
            {
                if (!managed.Contains(path) && !snapshotPaths.Contains(path))
                    throw new InvalidOperationException("관리 대상 설정 파일이 아닙니다: " + path);
            }

            var currentVersion = installation.Components.FirstOrDefault(item => item.Type == snapshot.Type)?.Version;
            safety = _snapshots.Capture(
                installation.RootPath,
                snapshot.Type,
                currentVersion,
                "BeforeSelectiveRestore",
                $"선택 복원 원본: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {selected.Length}개 파일");
            steps.Add("선택 복원 직전 안전 snapshot 저장: " + safety.ManifestPath);

            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                var state = _services.GetState(serviceName);
                serviceWasRunning = state.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                if (serviceWasRunning)
                {
                    await Task.Run(() => _services.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                    serviceStopped = true;
                    steps.Add("서비스 중지: " + serviceName);
                }
            }

            var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
            foreach (var path in selected)
            {
                var entry = snapshot.Files.FirstOrDefault(item => string.Equals(item.RelativePath, path, StringComparison.OrdinalIgnoreCase));
                var destination = SafeCombine(componentRoot, path);
                if (entry is null)
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                        steps.Add("현재에만 존재하는 설정 파일 삭제: " + path);
                    }
                    continue;
                }

                var source = SafeCombine(filesRoot, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                steps.Add("snapshot 파일 복원: " + path);
            }

            steps.Add($"선택 설정 적용 완료: {selected.Length}개 파일");
            steps.Add(await ValidateComponentAsync(installation, snapshot.Type, cancellationToken));

            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                serviceStopped = false;
                steps.Add("서비스 재시작 및 RUNNING 확인: " + serviceName);
            }

            try
            {
                afterRestore = _snapshots.Capture(
                    installation.RootPath,
                    snapshot.Type,
                    currentVersion,
                    "AfterSelectiveRestore",
                    $"선택 복원 원본: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {selected.Length}개 파일");
                steps.Add("선택 복원 후 설정 snapshot 저장: " + afterRestore.ManifestPath);
            }
            catch (Exception captureEx)
            {
                steps.Add("주의: 선택 복원 후 설정 snapshot 저장 실패: " + captureEx.Message);
            }

            return new ConfigSnapshotRestoreResult(true, false, safety.ManifestPath, afterRestore?.ManifestPath, steps);
        }
        catch (Exception ex)
        {
            var rolledBack = false;
            var error = ex.Message;
            if (safety is not null)
            {
                try
                {
                    ApplySnapshot(safety);
                    steps.Add("선택 복원 실패 후 직전 설정 snapshot 자동 원복 완료");
                    rolledBack = true;
                }
                catch (Exception rollbackEx)
                {
                    error += " / 자동 원복 실패: " + rollbackEx.Message;
                }
            }

            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                try
                {
                    if (!_services.GetState(serviceName).Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), CancellationToken.None);
                        steps.Add("서비스 원상복구 완료: " + serviceName);
                    }
                    serviceStopped = false;
                }
                catch (Exception restartEx)
                {
                    error += " / 서비스 원상복구 실패: " + restartEx.Message;
                }
            }

            return new ConfigSnapshotRestoreResult(false, rolledBack, safety?.ManifestPath, null, steps, error);
        }
        finally
        {
            if (serviceStopped && serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                try { _services.Start(serviceName, TimeSpan.FromSeconds(30)); } catch { }
            }
        }
    }
}
