using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IXamppInstallationDetector
{
    IReadOnlyList<string> FindCandidates();
    XamppInstallation Inspect(string rootPath, string discoverySource = "Manual");
}
