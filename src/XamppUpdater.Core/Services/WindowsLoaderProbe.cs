using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XamppUpdater.Core.Services;

public sealed record WindowsLoaderProbeResult(
    bool Success,
    int ErrorCode,
    string Message);

public static class WindowsLoaderProbe
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const uint LoadLibrarySearchUserDirs = 0x00000400;

    public static WindowsLoaderProbeResult TryLoad(string modulePath, IEnumerable<string> additionalDirectories)
    {
        if (!OperatingSystem.IsWindows())
            return new WindowsLoaderProbeResult(false, -1, "Windows에서만 로더 진단을 실행할 수 있습니다.");
        if (!File.Exists(modulePath))
            return new WindowsLoaderProbeResult(false, 2, "모듈 파일이 없습니다: " + modulePath);

        var cookies = new List<nint>();
        try
        {
            foreach (var directory in additionalDirectories
                         .Where(Directory.Exists)
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var cookie = AddDllDirectory(directory);
                if (cookie != nint.Zero) cookies.Add(cookie);
            }

            Marshal.SetLastPInvokeError(0);
            var handle = LoadLibraryExW(
                Path.GetFullPath(modulePath),
                nint.Zero,
                LoadLibrarySearchDllLoadDir | LoadLibrarySearchUserDirs | LoadLibrarySearchSystem32);
            if (handle != nint.Zero)
            {
                FreeLibrary(handle);
                return new WindowsLoaderProbeResult(true, 0, "LoadLibraryEx 성공");
            }

            var error = Marshal.GetLastPInvokeError();
            var message = error == 0 ? "알 수 없는 Windows 로더 오류" : new Win32Exception(error).Message;
            return new WindowsLoaderProbeResult(false, error, message);
        }
        catch (Exception ex)
        {
            return new WindowsLoaderProbeResult(false, -1, ex.Message);
        }
        finally
        {
            foreach (var cookie in cookies)
            {
                try { RemoveDllDirectory(cookie); }
                catch { }
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string lpFileName, nint hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDllDirectory(nint cookie);
}
