using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow : Window
{
    private readonly IXamppInstallationDetector _detector = new XamppInstallationDetector();
    private readonly IOnlineVersionCatalogService _onlineCatalog = new OnlineVersionCatalogService();
    private readonly IInstallationCompatibilityDetector _compatibilityDetector = new InstallationCompatibilityDetector();
    private readonly ICandidatePackageCatalogService _candidateCatalog = new CandidatePackageCatalogService();
    private readonly ISelectableVersionCatalogService _selectableVersionCatalog = new SelectableVersionCatalogService();
    private readonly IUpdatePreflightService _preflightService = new UpdatePreflightService();
    private readonly IUpdateBackupService _backupService = new UpdateBackupService();
    private readonly IPackagePreparationService _packagePreparationService = new PackagePreparationService();
    private readonly IConfigDiffService _configDiffService = new ConfigDiffService();
    private readonly Dictionary<XamppComponentType, UpdatePreflightReport> _preflightReports = new();
    private readonly Dictionary<XamppComponentType, PackagePreparationResult> _packageResults = new();

    private XamppInstallation? _lastInstallation;
    private InstallationCompatibilityProfile? _lastProfile;
    private OnlineVersionCatalog? _lastCatalog;
    private CandidatePackageCatalog? _lastCandidates;
    private SelectableVersionCatalog? _selectableVersions;
    private UpdateTargetCatalog? _targetCatalog;

    public MainWindow()
    {
        InitializeComponent();
        InitializeMariaDbSafeBackupUi();
        Loaded += async (_, _) => await AutoDetectAsync();
    }

    private async void AutoDetectButton_Click(object sender, RoutedEventArgs e) => await AutoDetectAsync();

    private async void InspectButton_Click(object sender, RoutedEventArgs e)
    {
        var path = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "XAMPP 설치 경로를 입력하거나 선택하세요.";
            return;
        }
        await InspectAsync(path, "Manual");
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "XAMPP 설치 폴더 선택", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        InstallPathComboBox.Text = dialog.FolderName;
        await InspectAsync(dialog.FolderName, "Manual");
    }

    private async void OnlineCheckButton_Click(object sender, RoutedEventArgs e) => await CheckOnlineVersionsAsync();

    private void TargetVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastInstallation is null || _lastProfile is null || sender is not ComboBox comboBox || comboBox.SelectedItem is not UpdateTargetOption target)
        {
            return;
        }

        _preflightReports.Remove(target.Type);
        _packageResults.Remove(target.Type);
        SetBackupEnabled(target.Type, false);
        SetDiffEnabled(target.Type, false);
        SetPackageEnabled(target.Type, target.PackageUrl is not null);
        RenderSelectedPlan(target);
        SetPreflightEnabled(target.Type, true);
        SetPreflightText(target.Type, target.PackageUrl is null
            ? "준비 점검: 실행 가능 / 패키지: 자동 다운로드 주소 추가 탐색 필요"
            : "준비 점검과 패키지 준비를 실행할 수 있습니다.");
    }

    private async void PreflightButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActionTarget(sender, out var type, out var target) || _lastInstallation is null) return;

        SetBusy(true, $"{type} 업데이트 준비 상태를 점검하는 중...");
        try
        {
            var report = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
            _preflightReports[type] = report;
            SetPreflightText(type, FormatPreflight(report));

            var canBackup = type == XamppComponentType.MariaDb
                ? CanRunMariaDbSafeBackup(report)
                : CanCreateBackup(report);
            SetBackupEnabled(type, canBackup);

            if (!canBackup && type == XamppComponentType.MariaDb)
            {
                AppendDetail(type, "백업 생성: 실행 중인 MariaDB를 안전하게 중지할 수 있는 Windows 서비스를 찾지 못했습니다.");
            }
            else if (type == XamppComponentType.MariaDb &&
                     (report.ProcessRunning || report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true))
            {
                AppendDetail(type, "백업 생성: 논리 백업 후 MariaDB 서비스를 자동 중지하고 물리 백업한 뒤 원래 상태로 복구합니다.");
            }

            StatusText.Text = $"{type} 준비 점검 완료: {report.CurrentVersion} → {report.TargetVersion}";
        }
        catch (Exception ex)
        {
            _preflightReports.Remove(type);
            SetBackupEnabled(type, false);
            SetPreflightText(type, $"준비 점검 실패: {ex.Message}");
            StatusText.Text = $"{type} 준비 점검 실패: {ex.Message}";
        }
        finally { SetBusy(false); }
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActionTarget(sender, out var type, out var target) || _lastInstallation is null) return;

        SetBusy(true, $"{type} 롤백 백업을 생성하는 중...");
        SetBackupEnabled(type, false);
        try
        {
            var report = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
            _preflightReports[type] = report;
            var result = await Task.Run(() => _backupService.CreateBackup(report));
            SetPreflightText(type, FormatPreflight(report));
            AppendDetail(type, $"백업 완료: {result.CopiedFiles:N0}개 / {FormatBytes(result.CopiedBytes)}");
            AppendDetail(type, $"manifest: {result.ManifestPath}");
            SetBackupEnabled(type, CanCreateBackup(report));
            StatusText.Text = $"{type} 롤백 백업 생성 완료";
        }
        catch (Exception ex)
        {
            AppendDetail(type, $"백업 생성 실패: {ex.Message}");
            SetBackupEnabled(type, _preflightReports.TryGetValue(type, out var report) && CanCreateBackup(report));
            StatusText.Text = $"{type} 백업 생성 실패: {ex.Message}";
        }
        finally { SetBusy(false); }
    }

    private async void PackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActionTarget(sender, out var type, out var target) || _lastProfile is null) return;

        SetBusy(true, $"{type} {target.Version} 패키지를 다운로드하고 검사하는 중...");
        SetPackageEnabled(type, false);
        SetDiffEnabled(type, false);
        try
        {
            var result = await _packagePreparationService.PrepareAsync(target, _lastProfile);
            _packageResults[type] = result;
            AppendDetail(type,
                $"패키지 준비 완료: {result.FileName} / {result.SizeText} / {CompatibilityEvaluator.FormatArchitecture(result.Architecture)}");
            AppendDetail(type, $"SHA256: {result.Sha256}");
            if (result.Warnings.Count > 0) AppendDetail(type, $"패키지 주의: {string.Join(" / ", result.Warnings)}");
            SetPackageEnabled(type, true);
            SetDiffEnabled(type, true);
            StatusText.Text = $"{type} {target.Version} 패키지 검증 완료";
        }
        catch (Exception ex)
        {
            _packageResults.Remove(type);
            AppendDetail(type, $"패키지 준비 실패: {ex.Message}");
            SetPackageEnabled(type, target.PackageUrl is not null);
            StatusText.Text = $"{type} 패키지 준비 실패: {ex.Message}";
        }
        finally { SetBusy(false); }
    }

    private async void DiffButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActionTarget(sender, out var type, out var target) || _lastInstallation is null || !_packageResults.TryGetValue(type, out var package)) return;

        SetBusy(true, $"{type} 기존 설정과 {target.Version} 기본 설정을 비교하는 중...");
        try
        {
            if (!_preflightReports.TryGetValue(type, out var preflight))
            {
                preflight = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
                _preflightReports[type] = preflight;
            }

            var diff = await Task.Run(() => _configDiffService.Compare(preflight, package));
            AppendDetail(type,
                $"설정 비교 ({diff.Baseline}): 동일 {diff.Same:N0} / 변경 {diff.Changed:N0} / 기존만 {diff.CurrentOnly:N0} / 신규만 {diff.TargetOnly:N0}");
            var important = diff.Items.Where(item => item.Kind != ConfigDiffKind.Same).Take(8).Select(item => $"{FormatDiffKind(item.Kind)} {item.Key}").ToArray();
            if (important.Length > 0) AppendDetail(type, $"확인 후보: {string.Join(", ", important)}");
            if (diff.Warnings.Count > 0) AppendDetail(type, $"비교 주의: {string.Join(" / ", diff.Warnings)}");
            StatusText.Text = $"{type} 설정 비교 완료";
        }
        catch (Exception ex)
        {
            AppendDetail(type, $"설정 비교 실패: {ex.Message}");
            StatusText.Text = $"{type} 설정 비교 실패: {ex.Message}";
        }
        finally { SetBusy(false); }
    }

    private async Task AutoDetectAsync()
    {
        SetBusy(true, "XAMPP 설치 경로 자동 감지 중...");
        try
        {
            var candidates = await Task.Run(_detector.FindCandidates);
            InstallPathComboBox.ItemsSource = candidates;
            if (candidates.Count == 0)
            {
                StatusText.Text = "자동 감지된 XAMPP 설치가 없습니다. 설치 폴더를 직접 선택하세요.";
                return;
            }
            InstallPathComboBox.SelectedIndex = 0;
            await InspectAsync(candidates[0], "Auto");
            if (candidates.Count > 1) StatusText.Text += $"  감지된 설치: {candidates.Count}개";
        }
        catch (Exception ex) { StatusText.Text = $"자동 감지 실패: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task InspectAsync(string path, string source)
    {
        SetBusy(true, "설치 버전과 호환성 정보를 확인하는 중...");
        try
        {
            var result = await Task.Run(() =>
            {
                var installation = _detector.Inspect(path, source);
                var profile = _compatibilityDetector.Detect(installation.RootPath, installation);
                return (installation, profile);
            });

            _lastInstallation = result.installation;
            _lastProfile = result.profile;
            _lastCandidates = null;
            _selectableVersions = null;
            _targetCatalog = null;
            _preflightReports.Clear();
            _packageResults.Clear();
            InstallPathComboBox.Text = result.installation.RootPath;
            RenderInstallation(result.installation);
            RenderCompatibilityProfile(result.profile);
            ClearCandidates();
            ClearTargetSelectors();
            ClearPreflight();
            if (_lastCatalog is not null) RenderOnlineCatalog(_lastCatalog);
            StatusText.Text = $"확인 완료: {result.installation.RootPath} ({result.installation.DiscoverySource})";
        }
        catch (Exception ex) { StatusText.Text = $"확인 실패: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task CheckOnlineVersionsAsync()
    {
        if (_lastInstallation is null || _lastProfile is null)
        {
            StatusText.Text = "먼저 XAMPP 설치 환경을 확인하세요.";
            return;
        }

        SetBusy(true, "최신 버전과 계열별 업데이트 버전을 확인하는 중...");
        try
        {
            var catalogTask = _onlineCatalog.GetLatestAsync();
            var candidateTask = _candidateCatalog.GetCandidatesAsync(_lastInstallation, _lastProfile);
            var selectableTask = _selectableVersionCatalog.GetAsync(_lastInstallation, _lastProfile);
            await Task.WhenAll(catalogTask, candidateTask, selectableTask);

            _lastCatalog = await catalogTask;
            _lastCandidates = await candidateTask;
            _selectableVersions = await selectableTask;
            _targetCatalog = UpdateTargetPlanner.BuildCatalog(_lastInstallation, _lastCatalog, _lastCandidates, _selectableVersions);
            RenderOnlineCatalog(_lastCatalog);
            RenderCandidates(_lastCandidates);
            RenderTargetSelectors(_targetCatalog);
            StatusText.Text = $"온라인 확인 완료: {_lastCatalog.CheckedAt:yyyy-MM-dd HH:mm:ss} / 계열별 선택 버전 {_selectableVersions.Entries.Count}개";
        }
        catch (Exception ex) { StatusText.Text = $"온라인 버전 확인 실패: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void RenderInstallation(XamppInstallation installation)
    {
        foreach (var component in installation.Components)
        {
            var versionText = component.IsInstalled
                ? component.Version is null ? "설치됨 / 버전 확인 실패" : $"설치 버전 {component.Version}"
                : "감지되지 않음";
            switch (component.Type)
            {
                case XamppComponentType.Apache:
                    ApacheVersionText.Text = versionText;
                    ApacheServiceText.Text = $"서비스: {component.ServiceName ?? "미등록"}";
                    ApachePathText.Text = $"경로: {component.ExecutablePath}";
                    break;
                case XamppComponentType.Php:
                    PhpVersionText.Text = versionText;
                    PhpPathText.Text = $"경로: {component.ExecutablePath}";
                    break;
                case XamppComponentType.MariaDb:
                    MariaDbVersionText.Text = versionText;
                    MariaDbServiceText.Text = $"서비스: {component.ServiceName ?? "미등록"}";
                    MariaDbPathText.Text = $"경로: {component.ExecutablePath}";
                    MariaDbDetailText.Text = component.Detail ?? string.Empty;
                    break;
            }
        }
    }

    private void RenderCompatibilityProfile(InstallationCompatibilityProfile profile)
    {
        var apacheIntegration = profile.ApachePhpIntegration.IsModuleLoaded ? $"PHP module: {profile.ApachePhpIntegration.ModuleName}" : "PHP module 미감지";
        ApacheEnvironmentText.Text = $"환경: {CompatibilityEvaluator.FormatArchitecture(profile.ApacheArchitecture)} / {apacheIntegration}";
        var threadSafety = profile.Php.ThreadSafe switch { true => "Thread Safe", false => "Non Thread Safe", null => "Thread Safety 미상" };
        PhpEnvironmentText.Text = $"환경: {CompatibilityEvaluator.FormatArchitecture(profile.PhpArchitecture)} / {threadSafety} / {profile.Php.Compiler ?? "Compiler 미상"}";
        MariaDbEnvironmentText.Text = $"환경: {CompatibilityEvaluator.FormatArchitecture(profile.MariaDbArchitecture)} / {profile.MariaDbSeries ?? "계열 미상"} 계열";
    }

    private void RenderOnlineCatalog(OnlineVersionCatalog catalog)
    {
        foreach (var component in catalog.Components)
        {
            var upstreamText = $"upstream 최신: {component.UpstreamLatestVersion ?? "확인 실패"}";
            var xamppText = $"XAMPP 공식: {component.XamppBundledVersion ?? "확인 실패"}";
            var installedVersion = _lastInstallation?.Components.FirstOrDefault(item => item.Type == component.Type)?.Version;
            var compatibilityText = _lastProfile is null ? component.CompatibilityNote : CompatibilityEvaluator.Evaluate(component.Type, installedVersion, component, _lastProfile);
            switch (component.Type)
            {
                case XamppComponentType.Apache: ApacheLatestText.Text = upstreamText; ApacheXamppText.Text = xamppText; ApacheCompatibilityText.Text = compatibilityText; break;
                case XamppComponentType.Php: PhpLatestText.Text = upstreamText; PhpXamppText.Text = xamppText; PhpCompatibilityText.Text = compatibilityText; break;
                case XamppComponentType.MariaDb: MariaDbLatestText.Text = upstreamText; MariaDbXamppText.Text = xamppText; MariaDbCompatibilityText.Text = compatibilityText; break;
            }
        }
    }

    private void RenderCandidates(CandidatePackageCatalog catalog)
    {
        foreach (var candidate in catalog.Candidates)
        {
            var text = FormatCandidate(candidate);
            switch (candidate.Type)
            {
                case XamppComponentType.Apache: ApacheCandidateText.Text = text; break;
                case XamppComponentType.Php: PhpCandidateText.Text = text; break;
                case XamppComponentType.MariaDb: MariaDbCandidateText.Text = text; break;
            }
        }
    }

    private void RenderTargetSelectors(UpdateTargetCatalog catalog)
    {
        SetTargets(ApacheTargetComboBox, catalog.Apache);
        SetTargets(PhpTargetComboBox, catalog.Php);
        SetTargets(MariaDbTargetComboBox, catalog.MariaDb);
    }

    private static void SetTargets(ComboBox comboBox, IReadOnlyList<UpdateTargetOption> targets)
    {
        comboBox.ItemsSource = targets;
        comboBox.IsEnabled = targets.Count > 0;
        comboBox.SelectedIndex = targets.Count > 0 ? 0 : -1;
    }

    private void RenderSelectedPlan(UpdateTargetOption target)
    {
        if (_lastInstallation is null || _lastProfile is null) return;
        var installedVersion = _lastInstallation.Components.First(item => item.Type == target.Type).Version;
        if (installedVersion is null)
        {
            SetPlanText(target.Type, "업데이트 경로: 현재 버전을 확인할 수 없습니다.");
            return;
        }
        var plan = UpdateTargetPlanner.BuildPlan(target.Type, installedVersion, target, _lastProfile);
        var text = FormatPlan(plan) + (target.PackageUrl is not null
            ? $"\n패키지: {target.PackageFileName ?? "공식 패키지 위치 확인됨"}"
            : "\n패키지: 업데이트 준비 단계에서 선택 버전에 맞는 Windows 패키지를 자동 탐색합니다.");
        SetPlanText(target.Type, text);
    }

    private static string FormatPlan(UpdatePlan plan)
    {
        var automatic = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.Automatic);
        var assisted = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.Assisted);
        var confirmations = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.UserConfirmation);
        return $"업데이트 경로: {plan.Summary}\n자동 {automatic} / 보조 {assisted} / 확인 {confirmations}\n{string.Join(" → ", plan.Steps.Select(step => step.Title))}";
    }

    private static string FormatPreflight(UpdatePreflightReport report)
    {
        var runtime = report.ServiceName is not null ? $"서비스 {report.ServiceName}: {report.ServiceState ?? "상태 미상"}" : report.ProcessRunning ? "프로세스: 실행 중" : "프로세스: 중지";
        var warning = report.Warnings.Count == 0 ? "주의사항 없음" : string.Join(" / ", report.Warnings);
        return $"준비 점검: {runtime}\n백업 예상: {report.BackupFileCount:N0}개 / {report.BackupSizeText}\n설정 manifest: {report.ConfigFiles.Count:N0}개\n백업 위치: {report.BackupDestination}\n주의: {warning}";
    }

    private static bool CanCreateBackup(UpdatePreflightReport report) =>
        report.Type != XamppComponentType.MariaDb ||
        (!report.ProcessRunning && report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) != true);

    private bool TryGetActionTarget(object sender, out XamppComponentType type, out UpdateTargetOption target)
    {
        type = default;
        target = null!;
        if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), true, out type)) return false;
        return GetTargetComboBox(type).SelectedItem is UpdateTargetOption selected && (target = selected) is not null;
    }

    private void AppendDetail(XamppComponentType type, string text)
    {
        var block = GetDetailTextBlock(type);
        block.Text = string.IsNullOrWhiteSpace(block.Text) ? text : block.Text + "\n" + text;
    }

    private TextBlock GetDetailTextBlock(XamppComponentType type) => type switch
    {
        XamppComponentType.Apache => ApachePreflightText,
        XamppComponentType.Php => PhpPreflightText,
        XamppComponentType.MariaDb => MariaDbPreflightText,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{value:N2} {units[unit]}";
    }

    private static string FormatDiffKind(ConfigDiffKind kind) => kind switch
    {
        ConfigDiffKind.Changed => "변경",
        ConfigDiffKind.CurrentOnly => "기존만",
        ConfigDiffKind.TargetOnly => "신규만",
        _ => "동일"
    };

    private void SetPlanText(XamppComponentType type, string text)
    {
        switch (type) { case XamppComponentType.Apache: ApachePlanText.Text = text; break; case XamppComponentType.Php: PhpPlanText.Text = text; break; case XamppComponentType.MariaDb: MariaDbPlanText.Text = text; break; }
    }

    private void SetPreflightText(XamppComponentType type, string text) => GetDetailTextBlock(type).Text = text;

    private void SetPreflightEnabled(XamppComponentType type, bool enabled)
    {
        switch (type) { case XamppComponentType.Apache: ApachePreflightButton.IsEnabled = enabled; break; case XamppComponentType.Php: PhpPreflightButton.IsEnabled = enabled; break; case XamppComponentType.MariaDb: MariaDbPreflightButton.IsEnabled = enabled; break; }
    }

    private void SetBackupEnabled(XamppComponentType type, bool enabled)
    {
        switch (type) { case XamppComponentType.Apache: ApacheBackupButton.IsEnabled = enabled; break; case XamppComponentType.Php: PhpBackupButton.IsEnabled = enabled; break; case XamppComponentType.MariaDb: MariaDbBackupButton.IsEnabled = enabled; break; }
    }

    private void SetPackageEnabled(XamppComponentType type, bool enabled)
    {
        switch (type) { case XamppComponentType.Apache: ApachePackageButton.IsEnabled = enabled; break; case XamppComponentType.Php: PhpPackageButton.IsEnabled = enabled; break; case XamppComponentType.MariaDb: MariaDbPackageButton.IsEnabled = enabled; break; }
    }

    private void SetDiffEnabled(XamppComponentType type, bool enabled)
    {
        switch (type) { case XamppComponentType.Apache: ApacheDiffButton.IsEnabled = enabled; break; case XamppComponentType.Php: PhpDiffButton.IsEnabled = enabled; break; case XamppComponentType.MariaDb: MariaDbDiffButton.IsEnabled = enabled; break; }
    }

    private ComboBox GetTargetComboBox(XamppComponentType type) => type switch
    {
        XamppComponentType.Apache => ApacheTargetComboBox,
        XamppComponentType.Php => PhpTargetComboBox,
        XamppComponentType.MariaDb => MariaDbTargetComboBox,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string FormatCandidate(PackageCandidate candidate)
    {
        var status = candidate.Status switch { CandidateCompatibilityStatus.Automatic => "자동 가능", CandidateCompatibilityStatus.Assisted => "보조 업데이트", CandidateCompatibilityStatus.ManualReview => "검토 후 진행", _ => "후보 없음" };
        if (candidate.Version is null) return $"실제 후보: 없음 ({candidate.Reason})";
        var details = new List<string>();
        if (candidate.Compiler is not null) details.Add(candidate.Compiler);
        if (candidate.ThreadSafe is not null) details.Add(candidate.ThreadSafe.Value ? "TS" : "NTS");
        if (candidate.Sha256 is not null) details.Add("SHA256");
        return $"실제 후보: {candidate.Version} [{status}]{(details.Count == 0 ? string.Empty : $" / {string.Join(" / ", details)}")}";
    }

    private void ClearCandidates()
    {
        ApacheCandidateText.Text = "실제 후보: -"; PhpCandidateText.Text = "실제 후보: -"; MariaDbCandidateText.Text = "실제 후보: -";
    }

    private void ClearTargetSelectors()
    {
        foreach (var comboBox in new[] { ApacheTargetComboBox, PhpTargetComboBox, MariaDbTargetComboBox }) { comboBox.ItemsSource = null; comboBox.IsEnabled = false; }
        ApachePlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
        PhpPlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
        MariaDbPlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
    }

    private void ClearPreflight()
    {
        foreach (var button in new[] { ApachePreflightButton, PhpPreflightButton, MariaDbPreflightButton, ApacheBackupButton, PhpBackupButton, MariaDbBackupButton, ApachePackageButton, PhpPackageButton, MariaDbPackageButton, ApacheDiffButton, PhpDiffButton, MariaDbDiffButton }) button.IsEnabled = false;
        ApachePreflightText.Text = "준비 점검: 대상 버전 선택 후 실행할 수 있습니다.";
        PhpPreflightText.Text = "준비 점검: 대상 버전 선택 후 실행할 수 있습니다.";
        MariaDbPreflightText.Text = "준비 점검: 대상 버전 선택 후 실행할 수 있습니다.";
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        InstallPathComboBox.IsEnabled = !isBusy;
        OnlineCheckButton.IsEnabled = !isBusy;
        if (message is not null) StatusText.Text = message;
    }
}