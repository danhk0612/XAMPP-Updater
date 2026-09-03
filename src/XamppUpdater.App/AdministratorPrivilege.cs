using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows;

namespace XamppUpdater.App;

internal static class AdministratorPrivilege
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool EnsureElevated(
        Window owner,
        string? xamppRoot,
        string action,
        string? resumeComponent = null,
        string? resumeVersion = null)
    {
        if (IsElevated) return true;
        return RelaunchWithApproval(owner, xamppRoot, action, resumeComponent, resumeVersion, null);
    }

    public static bool EnsureElevatedForRollback(
        Window owner,
        string? xamppRoot,
        string action,
        string component)
    {
        if (IsElevated) return true;
        return RelaunchWithApproval(owner, xamppRoot, action, null, null, component);
    }

    private static bool RelaunchWithApproval(
        Window owner,
        string? xamppRoot,
        string action,
        string? resumeComponent,
        string? resumeVersion,
        string? resumeRollbackComponent)
    {
        var answer = MessageBox.Show(
            owner,
            $"{action}에는 Windows 관리자 권한이 필요합니다.\n\n관리자 권한으로 XAMPP Updater를 다시 실행하시겠습니까?\n현재 XAMPP 경로와 진행할 작업은 새 창으로 전달됩니다.",
            "관리자 권한 필요",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return false;

        try
        {
            RelaunchElevated(xamppRoot, resumeComponent, resumeVersion, resumeRollbackComponent);
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(owner, "사용자가 관리자 권한 요청을 취소했습니다.", "관리자 권한", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "관리자 권한 재실행에 실패했습니다.\n\n" + ex.Message, "관리자 권한", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }

    private static void RelaunchElevated(
        string? xamppRoot,
        string? resumeComponent,
        string? resumeVersion,
        string? resumeRollbackComponent)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");
        var entryPath = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("애플리케이션 DLL 경로를 확인할 수 없습니다.");
        var start = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = processPath;
            start.ArgumentList.Add(entryPath);
        }
        else
        {
            start.FileName = processPath;
        }

        if (!string.IsNullOrWhiteSpace(xamppRoot))
        {
            start.ArgumentList.Add("--xampp-root");
            start.ArgumentList.Add(Path.GetFullPath(xamppRoot));
        }

        if (!string.IsNullOrWhiteSpace(resumeComponent) && !string.IsNullOrWhiteSpace(resumeVersion))
        {
            start.ArgumentList.Add("--resume-update");
            start.ArgumentList.Add(resumeComponent);
            start.ArgumentList.Add(resumeVersion);
        }

        if (!string.IsNullOrWhiteSpace(resumeRollbackComponent))
        {
            start.ArgumentList.Add("--resume-rollback");
            start.ArgumentList.Add(resumeRollbackComponent);
        }

        _ = Process.Start(start) ?? throw new InvalidOperationException("관리자 권한 프로세스를 시작하지 못했습니다.");
    }

    public static string? GetStartupXamppRoot()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--xampp-root", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    public static (string Component, string Version)? GetStartupResumeUpdate()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length - 2; i++)
        {
            if (string.Equals(args[i], "--resume-update", StringComparison.OrdinalIgnoreCase))
                return (args[i + 1], args[i + 2]);
        }
        return null;
    }

    public static string? GetStartupResumeRollback()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--resume-rollback", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
