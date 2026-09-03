using System.Diagnostics;
using System.Runtime.InteropServices;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

/// <summary>
/// 업데이트 작업이 직접 생성한 자식 프로세스를 감시한다.
/// 사용자 취소 시 남은 자식 프로세스를 종료하고, 하나의 자식 프로세스가
/// 제한 시간 이상 멈춰 있으면 해당 프로세스 트리를 종료한 뒤 업데이트 취소를 요청한다.
/// Windows 서비스는 SCM이 생성하므로 이 감시 대상에 포함되지 않는다.
/// </summary>
public static class UpdateExecutionWatchdog
{
    public static async Task<UpdateExecutionResult> ExecuteAsync(
        Func<CancellationToken, Task<UpdateExecutionResult>> operation,
        TimeSpan childProcessTimeout,
        CancellationToken cancellationToken = default)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (childProcessTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(childProcessTimeout));

        var rootPid = Environment.ProcessId;
        var baseline = GetDescendants(rootPid).ToHashSet();
        var firstSeen = new Dictionary<int, DateTimeOffset>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var monitorStop = new CancellationTokenSource();

        string? timedOutProcess = null;
        var sync = new object();

        using var cancelRegistration = linked.Token.Register(() =>
        {
            KillNewDescendants(rootPid, baseline);
        });

        var monitor = Task.Run(async () =>
        {
            while (!monitorStop.IsCancellationRequested && !linked.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var descendants = GetDescendants(rootPid)
                    .Where(pid => !baseline.Contains(pid))
                    .ToArray();

                foreach (var pid in descendants)
                {
                    if (!firstSeen.TryGetValue(pid, out var seen))
                    {
                        firstSeen[pid] = now;
                        continue;
                    }

                    if (now - seen < childProcessTimeout) continue;

                    var name = TryGetProcessName(pid) ?? $"PID {pid}";
                    lock (sync)
                    {
                        timedOutProcess ??= name;
                    }
                    KillProcessTree(pid);
                    linked.Cancel();
                    return;
                }

                foreach (var stale in firstSeen.Keys.Except(descendants).ToArray())
                    firstSeen.Remove(stale);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), monitorStop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, CancellationToken.None);

        UpdateExecutionResult result;
        try
        {
            result = await operation(linked.Token);
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitor; } catch { }
        }

        string? timeoutName;
        lock (sync) timeoutName = timedOutProcess;
        if (timeoutName is null) return result;

        var message = $"외부 프로세스 응답 시간 초과로 업데이트를 중단했습니다: {timeoutName} ({childProcessTimeout.TotalMinutes:N0}분)";
        return result with
        {
            Warnings = result.Warnings.Concat(new[] { message }).ToArray(),
            Error = string.IsNullOrWhiteSpace(result.Error) ? message : result.Error + " / " + message
        };
    }

    private static void KillNewDescendants(int rootPid, HashSet<int> baseline)
    {
        foreach (var pid in GetDescendants(rootPid).Where(pid => !baseline.Contains(pid)).OrderByDescending(pid => pid))
            KillProcessTree(pid);
    }

    private static void KillProcessTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 이미 종료됐거나 접근할 수 없는 프로세스는 무시한다.
        }
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyList<int> GetDescendants(int rootPid)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();

        var parentMap = SnapshotProcesses();
        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var pair in parentMap.Where(pair => pair.Value == parent))
            {
                if (result.Contains(pair.Key)) continue;
                result.Add(pair.Key);
                queue.Enqueue(pair.Key);
            }
        }
        return result;
    }

    private static Dictionary<int, int> SnapshotProcesses()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
        if (snapshot == InvalidHandleValue) return result;

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do
            {
                result[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    private const uint Th32csSnapprocess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
