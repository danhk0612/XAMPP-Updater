using System.IO;
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

    private XamppInstallation? _lastInstallation;
    private InstallationCompatibilityProfile? _lastProfile;
    private OnlineVersionCatalog? _lastCatalog;
    private CandidatePackageCatalog? _lastCandidates;
    private SelectableVersionCatalog? _selectableVersions;
    private UpdateTargetCatalog? _targetCatalog;
    private readonly Dictionary<XamppComponentType, string> _manualPackages = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await AutoDetectAsync();
    }

    private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoDetectAsync();
    }

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
        var dialog = new OpenFolderDialog
        {
            Title = "XAMPP 설치 폴더 선택",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        InstallPathComboBox.Text = dialog.FolderName;
        await InspectAsync(dialog.FolderName, "Manual");
    }

    private async void OnlineCheckButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckOnlineVersionsAsync();
    }

    private void TargetVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastInstallation is null || _lastProfile is null || sender is not ComboBox comboBox || comboBox.SelectedItem is not UpdateTargetOption target)
        {
            return;
        }

        RenderSelectedPlan(target);
    }

    private void SelectPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !Enum.TryParse<XamppComponentType>(button.Tag?.ToString(), true, out var type))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"{type} 업데이트 패키지 선택",
            Filter = "ZIP 패키지 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _manualPackages[type] = dialog.FileName;
        SetManualPackageText(type, $"직접 지정: {Path.GetFileName(dialog.FileName)}");

        var selected = GetTargetComboBox(type).SelectedItem as UpdateTargetOption;
        if (selected is not null)
        {
            RenderSelectedPlan(selected);
        }
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

            if (candidates.Count > 1)
            {
                StatusText.Text += $"  감지된 설치: {candidates.Count}개";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"자동 감지 실패: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
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
            _manualPackages.Clear();
            InstallPathComboBox.Text = result.installation.RootPath;
            RenderInstallation(result.installation);
            RenderCompatibilityProfile(result.profile);
            ClearCandidates();
            ClearTargetSelectors();
            ClearManualPackages();

            if (_lastCatalog is not null)
            {
                RenderOnlineCatalog(_lastCatalog);
            }

            StatusText.Text = $"확인 완료: {result.installation.RootPath} ({result.installation.DiscoverySource})";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"확인 실패: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckOnlineVersionsAsync()
    {
        if (_lastInstallation is null || _lastProfile is null)
        {
            StatusText.Text = "먼저 XAMPP 설치 환경을 확인하세요.";
            return;
        }

        SetBusy(true, "최신 버전, 전체 선택 버전과 실제 패키지 정보를 확인하는 중...");

        try
        {
            var catalogTask = _onlineCatalog.GetLatestAsync();
            var candidateTask = _candidateCatalog.GetCandidatesAsync(_lastInstallation, _lastProfile);
            var selectableTask = _selectableVersionCatalog.GetAsync(_lastInstallation, _lastProfile);
            await Task.WhenAll(catalogTask, candidateTask, selectableTask);

            _lastCatalog = await catalogTask;
            _lastCandidates = await candidateTask;
            _selectableVersions = await selectableTask;
            _targetCatalog = UpdateTargetPlanner.BuildCatalog(
                _lastInstallation,
                _lastCatalog,
                _lastCandidates,
                _selectableVersions);

            RenderOnlineCatalog(_lastCatalog);
            RenderCandidates(_lastCandidates);
            RenderTargetSelectors(_targetCatalog);
            StatusText.Text = $"온라인 확인 완료: {_lastCatalog.CheckedAt:yyyy-MM-dd HH:mm:ss} / 선택 가능 버전 {_selectableVersions.Entries.Count}개";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"온라인 버전 확인 실패: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
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
        var apacheIntegration = profile.ApachePhpIntegration.IsModuleLoaded
            ? $"PHP module: {profile.ApachePhpIntegration.ModuleName}"
            : "PHP module 미감지";
        ApacheEnvironmentText.Text = $"환경: {CompatibilityEvaluator.FormatArchitecture(profile.ApacheArchitecture)} / {apacheIntegration}";

        var threadSafety = profile.Php.ThreadSafe switch
        {
            true => "Thread Safe",
            false => "Non Thread Safe",
            null => "Thread Safety 미상"
        };
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
            var compatibilityText = _lastProfile is null
                ? component.CompatibilityNote
                : CompatibilityEvaluator.Evaluate(component.Type, installedVersion, component, _lastProfile);

            switch (component.Type)
            {
                case XamppComponentType.Apache:
                    ApacheLatestText.Text = upstreamText;
                    ApacheXamppText.Text = xamppText;
                    ApacheCompatibilityText.Text = compatibilityText;
                    break;
                case XamppComponentType.Php:
                    PhpLatestText.Text = upstreamText;
                    PhpXamppText.Text = xamppText;
                    PhpCompatibilityText.Text = compatibilityText;
                    break;
                case XamppComponentType.MariaDb:
                    MariaDbLatestText.Text = upstreamText;
                    MariaDbXamppText.Text = xamppText;
                    MariaDbCompatibilityText.Text = compatibilityText;
                    break;
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
                case XamppComponentType.Apache:
                    ApacheCandidateText.Text = text;
                    break;
                case XamppComponentType.Php:
                    PhpCandidateText.Text = text;
                    break;
                case XamppComponentType.MariaDb:
                    MariaDbCandidateText.Text = text;
                    break;
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
        if (_lastInstallation is null || _lastProfile is null)
        {
            return;
        }

        var installedVersion = _lastInstallation.Components.First(item => item.Type == target.Type).Version;
        if (installedVersion is null)
        {
            SetPlanText(target.Type, "업데이트 경로: 현재 버전을 확인할 수 없습니다.");
            return;
        }

        var plan = UpdateTargetPlanner.BuildPlan(target.Type, installedVersion, target, _lastProfile);
        var text = FormatPlan(plan);

        if (_manualPackages.TryGetValue(target.Type, out var manualPackage))
        {
            text += $"\n직접 지정 패키지: {Path.GetFileName(manualPackage)} — Phase 3에서 내부 버전/아키텍처/구조를 검증합니다.";
        }
        else if (target.PackageUrl is not null)
        {
            text += $"\n패키지: {target.PackageFileName ?? "공식 패키지 위치 확인됨"}";
        }
        else
        {
            text += "\n패키지: 자동 확인되지 않음 — 패키지 지정으로 계속 진행할 수 있습니다.";
        }

        SetPlanText(target.Type, text);
    }

    private static string FormatPlan(UpdatePlan plan)
    {
        var automatic = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.Automatic);
        var assisted = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.Assisted);
        var confirmations = plan.Steps.Count(step => step.Kind == UpdatePlanStepKind.UserConfirmation);
        var package = $"자동 {automatic} / 보조 {assisted} / 확인 {confirmations}";
        var mainSteps = string.Join(" → ", plan.Steps.Select(step => step.Title));
        return $"업데이트 경로: {plan.Summary}\n{package}\n{mainSteps}";
    }

    private void SetPlanText(XamppComponentType type, string text)
    {
        switch (type)
        {
            case XamppComponentType.Apache:
                ApachePlanText.Text = text;
                break;
            case XamppComponentType.Php:
                PhpPlanText.Text = text;
                break;
            case XamppComponentType.MariaDb:
                MariaDbPlanText.Text = text;
                break;
        }
    }

    private static string FormatCandidate(PackageCandidate candidate)
    {
        var status = candidate.Status switch
        {
            CandidateCompatibilityStatus.Automatic => "자동 가능",
            CandidateCompatibilityStatus.Assisted => "보조 업데이트",
            CandidateCompatibilityStatus.ManualReview => "검토 후 진행",
            _ => "후보 없음"
        };

        if (candidate.Version is null)
        {
            return $"실제 후보: 없음 ({candidate.Reason})";
        }

        var details = new List<string>();
        if (candidate.Compiler is not null)
        {
            details.Add(candidate.Compiler);
        }
        if (candidate.ThreadSafe is not null)
        {
            details.Add(candidate.ThreadSafe.Value ? "TS" : "NTS");
        }
        if (candidate.Sha256 is not null)
        {
            details.Add("SHA256");
        }

        var suffix = details.Count == 0 ? string.Empty : $" / {string.Join(" / ", details)}";
        return $"실제 후보: {candidate.Version} [{status}]{suffix}";
    }

    private ComboBox GetTargetComboBox(XamppComponentType type)
    {
        return type switch
        {
            XamppComponentType.Apache => ApacheTargetComboBox,
            XamppComponentType.Php => PhpTargetComboBox,
            XamppComponentType.MariaDb => MariaDbTargetComboBox,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private void SetManualPackageText(XamppComponentType type, string text)
    {
        switch (type)
        {
            case XamppComponentType.Apache:
                ApacheManualPackageText.Text = text;
                break;
            case XamppComponentType.Php:
                PhpManualPackageText.Text = text;
                break;
            case XamppComponentType.MariaDb:
                MariaDbManualPackageText.Text = text;
                break;
        }
    }

    private void ClearCandidates()
    {
        ApacheCandidateText.Text = "실제 후보: -";
        PhpCandidateText.Text = "실제 후보: -";
        MariaDbCandidateText.Text = "실제 후보: -";
    }

    private void ClearTargetSelectors()
    {
        foreach (var comboBox in new[] { ApacheTargetComboBox, PhpTargetComboBox, MariaDbTargetComboBox })
        {
            comboBox.ItemsSource = null;
            comboBox.IsEnabled = false;
        }

        ApachePlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
        PhpPlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
        MariaDbPlanText.Text = "업데이트 경로: 온라인 확인 후 선택할 수 있습니다.";
    }

    private void ClearManualPackages()
    {
        ApacheManualPackageText.Text = "직접 지정: 없음";
        PhpManualPackageText.Text = "직접 지정: 없음";
        MariaDbManualPackageText.Text = "직접 지정: 없음";
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        InstallPathComboBox.IsEnabled = !isBusy;
        OnlineCheckButton.IsEnabled = !isBusy;
        if (message is not null)
        {
            StatusText.Text = message;
        }
    }
}
