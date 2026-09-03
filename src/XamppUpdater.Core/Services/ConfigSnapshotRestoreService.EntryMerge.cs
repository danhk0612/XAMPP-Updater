using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class ConfigSnapshotRestoreService
{
    public async Task<ConfigSnapshotRestoreResult> MergeEntriesAsync(
        XamppInstallation installation,
        ConfigSnapshotManifest snapshot,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> selections,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0 || selections.All(x => x.Value.Count == 0))
            throw new ArgumentException("선택된 설정 항목이 없습니다.", nameof(selections));

        var steps = new List<string>();
        ConfigSnapshotManifest? safety = null;
        ConfigSnapshotManifest? after = null;
        var serviceName = ResolveServiceName(installation, snapshot.Type);
        var serviceWasRunning = false;
        var serviceStopped = false;
        var merger = new ConfigEntryMergeService();
        try
        {
            ValidateSnapshot(installation, snapshot);
            var version = installation.Components.FirstOrDefault(x => x.Type == snapshot.Type)?.Version;
            safety = _snapshots.Capture(installation.RootPath, snapshot.Type, version, "BeforeEntryMerge",
                $"항목 병합 원본: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            steps.Add("항목 병합 직전 안전 snapshot 저장: " + safety.ManifestPath);

            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                serviceWasRunning = _services.GetState(serviceName).Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                if (serviceWasRunning)
                {
                    await Task.Run(() => _services.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                    serviceStopped = true;
                    steps.Add("서비스 중지: " + serviceName);
                }
            }

            var componentRoot = GetComponentRoot(installation.RootPath, snapshot.Type);
            var snapshotFilesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
            var applied = 0;
            foreach (var pair in selections.Where(x => x.Value.Count > 0))
            {
                var currentPath = SafeCombine(componentRoot, pair.Key);
                var snapshotPath = SafeCombine(snapshotFilesRoot, pair.Key);
                if (!File.Exists(currentPath) || !File.Exists(snapshotPath))
                    throw new FileNotFoundException("항목 병합 대상 파일을 찾을 수 없습니다: " + pair.Key);
                var merged = merger.ApplySelections(File.ReadAllText(currentPath), File.ReadAllText(snapshotPath), snapshot.Type, pair.Value);
                File.WriteAllText(currentPath, merged, new System.Text.UTF8Encoding(false));
                applied += pair.Value.Count;
                steps.Add($"항목 병합: {pair.Key} / {pair.Value.Count}개");
            }

            steps.Add(await ValidateComponentAsync(installation, snapshot.Type, cancellationToken));
            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                serviceStopped = false;
                steps.Add("서비스 재시작 및 RUNNING 확인: " + serviceName);
            }

            try
            {
                after = _snapshots.Capture(installation.RootPath, snapshot.Type, version, "AfterEntryMerge",
                    $"항목 병합 원본: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {applied}개 항목");
                steps.Add("항목 병합 후 snapshot 저장: " + after.ManifestPath);
            }
            catch (Exception ex) { steps.Add("주의: 항목 병합 후 snapshot 저장 실패: " + ex.Message); }

            return new ConfigSnapshotRestoreResult(true, false, safety.ManifestPath, after?.ManifestPath, steps);
        }
        catch (Exception ex)
        {
            var rolledBack = false;
            var error = ex.Message;
            if (safety is not null)
            {
                try { ApplySnapshot(safety); rolledBack = true; steps.Add("항목 병합 실패 후 직전 설정으로 자동 원복 완료"); }
                catch (Exception rb) { error += " / 자동 원복 실패: " + rb.Message; }
            }
            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                try
                {
                    if (!_services.GetState(serviceName).Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                        await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), CancellationToken.None);
                    serviceStopped = false;
                }
                catch (Exception restart) { error += " / 서비스 원상복구 실패: " + restart.Message; }
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
