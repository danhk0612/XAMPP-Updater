using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XamppUpdater.Core.Services;

public interface IWindowsServiceController
{
    string GetState(string serviceName);
    void Stop(string serviceName, TimeSpan timeout);
    void Start(string serviceName, TimeSpan timeout);
}

public sealed class WindowsServiceController : IWindowsServiceController
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScStatusProcessInfo = 0;

    public string GetState(string serviceName) => WindowsServiceStateReader.Read(serviceName);

    public void Stop(string serviceName, TimeSpan timeout)
    {
        EnsureWindows();
        using var handle = Open(serviceName, ServiceQueryStatus | ServiceStop);
        var current = QueryStatus(handle.DangerousGetHandle());
        if (current.dwCurrentState == 1)
        {
            return;
        }

        if (current.dwCurrentState != 3 && !ControlService(handle.DangerousGetHandle(), ServiceControlStop, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"서비스 중지 요청 실패: {serviceName}");
        }

        WaitForState(handle.DangerousGetHandle(), 1, timeout, serviceName, starting: false);
    }

    public void Start(string serviceName, TimeSpan timeout)
    {
        EnsureWindows();
        using var handle = Open(serviceName, ServiceQueryStatus | ServiceStart);
        var current = QueryStatus(handle.DangerousGetHandle());
        if (current.dwCurrentState == 4)
        {
            return;
        }

        if (current.dwCurrentState != 2 && !StartService(handle.DangerousGetHandle(), 0, null))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"서비스 시작 요청 실패: {serviceName}");
        }

        WaitForState(handle.DangerousGetHandle(), 4, timeout, serviceName, starting: true);
    }

    internal static bool IsTerminalStateForOperation(uint currentState, bool starting) =>
        starting ? currentState == 4 : currentState == 1;

    private static void WaitForState(
        IntPtr service,
        uint targetState,
        TimeSpan timeout,
        string serviceName,
        bool starting)
    {
        var deadline = DateTime.UtcNow + timeout;
        SERVICE_STATUS_PROCESS last = default;

        while (DateTime.UtcNow < deadline)
        {
            last = QueryStatus(service);
            if (last.dwCurrentState == targetState)
            {
                return;
            }

            if (starting && last.dwCurrentState == 1)
            {
                throw new InvalidOperationException(
                    $"서비스 시작 후 즉시 STOPPED 상태가 되었습니다: {serviceName} / " +
                    $"Win32ExitCode={last.dwWin32ExitCode} / ServiceSpecificExitCode={last.dwServiceSpecificExitCode} / PID={last.dwProcessId}");
            }

            Thread.Sleep(200);
        }

        var state = WindowsServiceStateReader.MapState(last.dwCurrentState);
        throw new TimeoutException(
            $"서비스 상태 변경 시간 초과: {serviceName} → {WindowsServiceStateReader.MapState(targetState)} / " +
            $"현재={state} / Win32ExitCode={last.dwWin32ExitCode} / ServiceSpecificExitCode={last.dwServiceSpecificExitCode} / PID={last.dwProcessId}");
    }

    private static SERVICE_STATUS_PROCESS QueryStatus(IntPtr service)
    {
        var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        if (!QueryServiceStatusEx(service, ScStatusProcessInfo, out var status, size, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "서비스 상태 확인 실패");
        }

        return status;
    }

    private static ServiceHandle Open(string serviceName, uint access)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Service Control Manager를 열 수 없습니다.");
        }

        try
        {
            var service = OpenService(manager, serviceName, access);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows 서비스를 열 수 없습니다: {serviceName}");
            }

            return new ServiceHandle(service);
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows 서비스 제어는 Windows에서만 사용할 수 있습니다.");
        }
    }

    private sealed class ServiceHandle : SafeHandle
    {
        public ServiceHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, uint argumentCount, string[]? arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        out SERVICE_STATUS_PROCESS status,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
