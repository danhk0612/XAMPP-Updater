using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IComponentVersionDetector
{
    ComponentVersionResult Detect(XamppComponentType type, string executablePath);
}

public sealed record ComponentVersionResult(string? Version, string Output, string? Detail = null);
