using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed record UpdateProgress(
    XamppComponentType Type,
    string Stage,
    string Message,
    int? Percent = null,
    bool IsRollback = false);

public static class UpdateProgressReporter
{
    public static event Action<UpdateProgress>? ProgressReported;

    public static void Report(
        XamppComponentType type,
        string stage,
        string message,
        int? percent = null,
        bool isRollback = false)
    {
        try
        {
            ProgressReported?.Invoke(new UpdateProgress(type, stage, message, percent, isRollback));
        }
        catch
        {
            // Progress reporting must never affect update/rollback execution.
        }
    }
}
