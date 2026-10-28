using System.Net;
using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

/// <summary>Conservative Phase A reader. Never guesses conditional/include/vhost configuration.</summary>
internal static class ApachePhpProbeTargetSelector
{
    internal sealed record Target(string DocumentRoot, IPAddress Address, int Port);

    internal static Target? Select(string config, string apacheRoot, out string reason, bool requireListener = true)
    {
        reason = "CONFIG_UNSUPPORTED";
        var lines = config.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
        // Includes, defines and conditional routing need active-runtime discovery (Phase B).
        // Directory/Files/Location blocks may contain access rules; leave these to the HTTP response.
        var depth = 0;
        var roots = new List<string>();
        var listeners = new List<(IPAddress Address, int Port)>();
        var serverRoot = Path.GetFullPath(apacheRoot);
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^(Include(?:Optional)?|Define|UnDefine|Use|AccessFileName)\b|^<(?:VirtualHost|If\w*|Else\w*|Macro)\b", RegexOptions.IgnoreCase))
                return null;
            if (line.StartsWith("</", StringComparison.Ordinal)) { if (--depth < 0) return null; continue; }
            if (line.StartsWith('<')) { depth++; continue; }
            var directive = Regex.Match(line, @"^(?<name>\S+)\s+(?<value>.*)$");
            if (!directive.Success) continue;
            var name = directive.Groups["name"].Value;
            var value = directive.Groups["value"].Value;
            if (name.StartsWith("ProxyPass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("RewriteRule", StringComparison.OrdinalIgnoreCase) ||
                (name.Equals("SetHandler", StringComparison.OrdinalIgnoreCase) && value.Contains("proxy:", StringComparison.OrdinalIgnoreCase)))
                return null;
            if (name.Equals("ServerRoot", StringComparison.OrdinalIgnoreCase))
            {
                if (depth != 0 || roots.Count != 0 || !TryPath(value, apacheRoot, out var root)) return null;
                serverRoot = root!;
            }
            if (name.Equals("DocumentRoot", StringComparison.OrdinalIgnoreCase))
            {
                if (depth != 0 || !TryPath(value, serverRoot, out var root)) return null;
                roots.Add(root!);
            }
            if (name.Equals("Listen", StringComparison.OrdinalIgnoreCase))
            {
                if (depth != 0 || !TryListener(value, out var address, out var port)) return null;
                listeners.Add((address!, port));
            }
            // Do not send HTTP to TLS or proxy-only frontends, including nonstandard TLS ports.
            if (name.Equals("SSLEngine", StringComparison.OrdinalIgnoreCase) && !value.Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (depth != 0 || roots.Count != 1 || listeners.Count > 1 || (requireListener && listeners.Count != 1)) return null;
        reason = "TARGET_SELECTED";
        return new Target(roots[0], listeners.Count == 0 ? IPAddress.Loopback : listeners[0].Address,
            listeners.Count == 0 ? 0 : listeners[0].Port);
    }

    internal static bool TryPath(string value, string serverRoot, out string? path)
    {
        path = null;
        var match = Regex.Match(value, "^(?:\"(?<path>[^\"]+)\"|(?<path>[^\\s\"']+))\\s*$");
        if (!match.Success) return false;
        var raw = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
        if (raw.IndexOfAny(['$', '%', '\0', '*', '?']) >= 0 || raw.StartsWith("\\\\", StringComparison.Ordinal) ||
            raw.StartsWith(new string(Path.DirectorySeparatorChar, 2), StringComparison.Ordinal)) return false;
        if (raw.Split(['/', '\\']).Any(part => part == "..")) return false;
        try
        {
            if (Path.IsPathRooted(raw) && !Path.IsPathFullyQualified(raw)) return false;
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw, Path.GetFullPath(serverRoot)));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    internal static bool TryListener(string value, out IPAddress? address, out int port)
    {
        address = IPAddress.Loopback;
        port = 0;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || (parts.Length == 2 && !parts[1].Equals("http", StringComparison.OrdinalIgnoreCase))) return false;
        var raw = parts[0];
        var portText = raw;
        if (raw.Contains(':'))
        {
            var split = raw.LastIndexOf(':');
            var host = raw[..split].Trim('[', ']');
            portText = raw[(split + 1)..];
            if (host == "*" || host == "0.0.0.0") address = IPAddress.Loopback;
            else if (host == "::") address = IPAddress.IPv6Loopback;
            else if (!IPAddress.TryParse(host, out address) || !IPAddress.IsLoopback(address)) return false;
        }
        return int.TryParse(portText, out port) && port is >= 1 and <= 65535 && (port != 443 || parts.Length == 2);
    }

    internal static bool IsSafeWebRoot(string path)
    {
        if (!Directory.Exists(path)) return false;
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                File.Exists(Path.Combine(directory.FullName, ".htaccess"))) return false;
        return true;
    }
}
