using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XamppUpdater.Core.Services;

internal static class WindowsServiceStateReader
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;

    public static string Read(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "지원되지 않음";
        }

        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return $"확인 실패 ({Marshal.GetLastWin32Error()})";
        }

        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return $"확인 실패 ({Marshal.GetLastWin32Error()})";
            }

            try
            {
                var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
                if (!QueryServiceStatusEx(
                        service,
                        ScStatusProcessInfo,
                        out var status,
                        size,
                        out _))
                {
                    return $"확인 실패 ({Marshal.GetLastWin32Error()})";
                }

                return MapState(status.dwCurrentState);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    internal static string MapState(uint state)
    {
        return state switch
        {
            1 => "STOPPED",
            2 => "START_PENDING",
            3 => "STOP_PENDING",
            4 => "RUNNING",
            5 => "CONTINUE_PENDING",
            6 => "PAUSE_PENDING",
            7 => "PAUSED",
            _ => $"UNKNOWN ({state})"
        };
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
    private static extern IntPtr OpenSCManager(
        string? lpMachineName,
        string? lpDatabaseName,
        uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(
        IntPtr hSCManager,
        string lpServiceName,
        uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr hService,
        int infoLevel,
        out SERVICE_STATUS_PROCESS lpBuffer,
        int cbBufSize,
        out int pcbBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);
}
