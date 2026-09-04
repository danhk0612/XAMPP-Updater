namespace XamppUpdater.Core.Services;

public sealed record PhpIniOwnedPathReconcileResult(
    string IniText,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Reconciles php.ini paths that belong to the PHP installation itself.
/// Only paths that resolve inside the configured XAMPP PHP root are moved.
/// External paths are deliberately left untouched.
/// </summary>
public static class PhpIniOwnedPathReconciler
{
    private enum ValueKind
    {
        Single,
        PathList,
        SessionSavePath
    }

    private enum Materialization
    {
        None,
        CopyFile,
        CopyDirectoryMissing,
        CreateDirectory,
        CreateParentDirectory
    }

    private sealed record PathSpec(
        ValueKind Kind,
        Materialization Materialization,
        bool AllowRelative = true);

    private static readonly IReadOnlyDictionary<string, PathSpec> Specs =
        new Dictionary<string, PathSpec>(StringComparer.OrdinalIgnoreCase)
        {
            // Runtime/package-owned directory. Rebase only; never copy old extension DLLs wholesale.
            ["extension_dir"] = new(ValueKind.Single, Materialization.None),

            // Referenced files that should follow the PHP installation when they actually exist there.
            ["browscap"] = new(ValueKind.Single, Materialization.CopyFile),
            ["curl.cainfo"] = new(ValueKind.Single, Materialization.CopyFile),
            ["openssl.cafile"] = new(ValueKind.Single, Materialization.CopyFile),
            ["tidy.default_config"] = new(ValueKind.Single, Materialization.CopyFile),
            ["auto_prepend_file"] = new(ValueKind.Single, Materialization.CopyFile),
            ["auto_append_file"] = new(ValueKind.Single, Materialization.CopyFile),
            ["opcache.preload"] = new(ValueKind.Single, Materialization.CopyFile),

            // Directories whose contents are meaningful user/runtime data rather than PHP binaries.
            ["openssl.capath"] = new(ValueKind.Single, Materialization.CopyDirectoryMissing),
            ["include_path"] = new(ValueKind.PathList, Materialization.CopyDirectoryMissing),

            // Security path lists are rebased but are never created or copied implicitly.
            ["open_basedir"] = new(ValueKind.PathList, Materialization.None),

            // Runtime scratch/cache directories. Preserve the path, not stale contents.
            ["session.save_path"] = new(ValueKind.SessionSavePath, Materialization.CreateDirectory),
            ["upload_tmp_dir"] = new(ValueKind.Single, Materialization.CreateDirectory),
            ["sys_temp_dir"] = new(ValueKind.Single, Materialization.CreateDirectory),
            ["soap.wsdl_cache_dir"] = new(ValueKind.Single, Materialization.CreateDirectory),
            ["opcache.file_cache"] = new(ValueKind.Single, Materialization.CreateDirectory),

            // Log files should start clean, but their parent directory must remain usable.
            ["error_log"] = new(ValueKind.Single, Materialization.CreateParentDirectory),
            ["mail.log"] = new(ValueKind.Single, Materialization.CreateParentDirectory)
        };

    public static PhpIniOwnedPathReconcileResult Reconcile(
        string iniText,
        string configuredPhpRoot,
        string sourceContentRoot,
        string destinationPhpRoot,
        bool materialize = true)
    {
        if (string.IsNullOrWhiteSpace(iniText) ||
            string.IsNullOrWhiteSpace(configuredPhpRoot) ||
            string.IsNullOrWhiteSpace(sourceContentRoot) ||
            string.IsNullOrWhiteSpace(destinationPhpRoot))
        {
            return new PhpIniOwnedPathReconcileResult(iniText, Array.Empty<string>());
        }

        string configuredRoot;
        string sourceRoot;
        string destinationRoot;
        try
        {
            configuredRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPhpRoot));
            sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceContentRoot));
            destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPhpRoot));
        }
        catch
        {
            return new PhpIniOwnedPathReconcileResult(iniText, Array.Empty<string>());
        }

        var warnings = new List<string>();
        var lines = iniText.Replace("\r\n", "\n").Split('\n');
        var changed = false;

        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryParseDirective(lines[index], out var parsed)) continue;
            if (!Specs.TryGetValue(parsed.Name, out var spec)) continue;

            var rewritten = spec.Kind switch
            {
                ValueKind.PathList => RewritePathList(parsed.Value, parsed.Name, spec, configuredRoot, sourceRoot, destinationRoot, materialize, warnings),
                ValueKind.SessionSavePath => RewriteSessionSavePath(parsed.Value, parsed.Name, spec, configuredRoot, sourceRoot, destinationRoot, materialize, warnings),
                _ => RewriteSinglePath(parsed.Value, parsed.Name, spec, configuredRoot, sourceRoot, destinationRoot, materialize, warnings)
            };

            if (string.Equals(rewritten, parsed.Value, StringComparison.Ordinal)) continue;
            lines[index] = parsed.Rebuild(rewritten);
            changed = true;
        }

        return new PhpIniOwnedPathReconcileResult(
            changed ? string.Join(Environment.NewLine, lines) : iniText,
            warnings);
    }

    /// <summary>
    /// Resolves a configured path only when it belongs to the PHP installation.
    /// The configured root and the physical source root are intentionally separate:
    /// after a directory swap php.ini can still say C:\xampp\php while the old files
    /// physically live in .xampp-updater-php-old-... .
    /// </summary>
    public static bool TryMapOwnedPath(
        string configuredPath,
        string configuredPhpRoot,
        string sourceContentRoot,
        string destinationPhpRoot,
        out string sourcePath,
        out string destinationPath,
        bool allowRelative = true)
    {
        sourcePath = string.Empty;
        destinationPath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath)) return false;

        var value = configuredPath.Trim().Trim('"', '\'');
        if (value.Length == 0 ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("syslog", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("://", StringComparison.OrdinalIgnoreCase) ||
            value.Contains('%'))
        {
            return false;
        }

        try
        {
            var configuredRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPhpRoot));
            var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceContentRoot));
            var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPhpRoot));

            if (Path.IsPathFullyQualified(value))
            {
                var full = Path.GetFullPath(value);
                if (TryRelativeInside(full, configuredRoot, out var relative) ||
                    TryRelativeInside(full, sourceRoot, out relative) ||
                    TryRelativeInside(full, destinationRoot, out relative))
                {
                    return BuildMapped(relative, sourceRoot, destinationRoot, out sourcePath, out destinationPath);
                }
                return false;
            }

            if (OperatingSystem.IsWindows() && IsDriveRootRelative(value))
            {
                var normalized = value.Replace('/', '\\').TrimStart('\\');
                var driveRoot = Path.GetPathRoot(configuredRoot);
                if (string.IsNullOrWhiteSpace(driveRoot) || configuredRoot.Length <= driveRoot.Length) return false;
                var configuredFromDrive = configuredRoot[driveRoot.Length..].TrimStart('\\', '/');

                if (normalized.Equals(configuredFromDrive, StringComparison.OrdinalIgnoreCase))
                    return BuildMapped(string.Empty, sourceRoot, destinationRoot, out sourcePath, out destinationPath);
                if (!normalized.StartsWith(configuredFromDrive + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                var relative = normalized[(configuredFromDrive.Length + 1)..];
                return BuildMapped(relative, sourceRoot, destinationRoot, out sourcePath, out destinationPath);
            }

            if (!allowRelative || value == "." || value == "..") return false;
            var normalizedRelative = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedRelative)) return false;
            var sourceCandidate = Path.GetFullPath(Path.Combine(sourceRoot, normalizedRelative));
            var destinationCandidate = Path.GetFullPath(Path.Combine(destinationRoot, normalizedRelative));
            if (!IsInsideRoot(sourceCandidate, sourceRoot) || !IsInsideRoot(destinationCandidate, destinationRoot)) return false;

            // Relative paths are ambiguous in PHP. Treat them as PHP-owned only when the
            // source or target package provides concrete evidence that they belong there.
            if (!File.Exists(sourceCandidate) && !Directory.Exists(sourceCandidate) &&
                !File.Exists(destinationCandidate) && !Directory.Exists(destinationCandidate))
            {
                return false;
            }

            sourcePath = sourceCandidate;
            destinationPath = destinationCandidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string RewriteSinglePath(
        string value,
        string directive,
        PathSpec spec,
        string configuredRoot,
        string sourceRoot,
        string destinationRoot,
        bool materialize,
        List<string> warnings)
    {
        if (!TryMapOwnedPath(value, configuredRoot, sourceRoot, destinationRoot, out var source, out var destination, spec.AllowRelative))
            return value;

        if (materialize) Materialize(directive, spec.Materialization, source, destination, sourceRoot, destinationRoot, warnings);
        return destination;
    }

    private static string RewritePathList(
        string value,
        string directive,
        PathSpec spec,
        string configuredRoot,
        string sourceRoot,
        string destinationRoot,
        bool materialize,
        List<string> warnings)
    {
        var parts = value.Split(';');
        var changed = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var original = parts[i];
            var trimmed = original.Trim();
            if (trimmed.Length == 0 || trimmed == ".") continue;

            var quote = trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\'') && trimmed[^1] == trimmed[0]
                ? trimmed[0]
                : '\0';
            var pathValue = quote == '\0' ? trimmed : trimmed[1..^1];
            if (!TryMapOwnedPath(pathValue, configuredRoot, sourceRoot, destinationRoot, out var source, out var destination, spec.AllowRelative))
                continue;

            if (materialize) Materialize(directive, spec.Materialization, source, destination, sourceRoot, destinationRoot, warnings);
            var replacement = quote == '\0' ? destination : quote + destination + quote;
            var leading = original[..(original.Length - original.TrimStart().Length)];
            var trailing = original[(original.TrimEnd().Length)..];
            parts[i] = leading + replacement + trailing;
            changed = true;
        }
        return changed ? string.Join(';', parts) : value;
    }

    private static string RewriteSessionSavePath(
        string value,
        string directive,
        PathSpec spec,
        string configuredRoot,
        string sourceRoot,
        string destinationRoot,
        bool materialize,
        List<string> warnings)
    {
        // PHP supports session.save_path forms such as "N;MODE;/path". Only the
        // final path segment is a filesystem path; the prefix must remain intact.
        var separator = value.LastIndexOf(';');
        var prefix = separator >= 0 ? value[..(separator + 1)] : string.Empty;
        var pathValue = separator >= 0 ? value[(separator + 1)..] : value;
        var trimmed = pathValue.Trim();
        if (trimmed.Length == 0) return value;

        if (!TryMapOwnedPath(trimmed, configuredRoot, sourceRoot, destinationRoot, out var source, out var destination, spec.AllowRelative))
            return value;
        if (materialize) Materialize(directive, spec.Materialization, source, destination, sourceRoot, destinationRoot, warnings);
        return prefix + destination;
    }

    private static void Materialize(
        string directive,
        Materialization mode,
        string source,
        string destination,
        string sourceRoot,
        string destinationRoot,
        List<string> warnings)
    {
        if (!IsInsideRoot(destination, destinationRoot)) return;

        try
        {
            switch (mode)
            {
                case Materialization.None:
                    return;

                case Materialization.CopyFile:
                    if (File.Exists(destination)) return;
                    if (!File.Exists(source)) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: false);
                    warnings.Add($"PHP 소유 파일 자동 보존 ({directive}): {source} → {destination}");
                    return;

                case Materialization.CopyDirectoryMissing:
                    if (!Directory.Exists(source) || !IsInsideRoot(source, sourceRoot)) return;
                    if (PathEquals(source, sourceRoot) || PathEquals(destination, destinationRoot)) return;
                    CopyDirectoryMissing(source, destination);
                    warnings.Add($"PHP 소유 디렉터리 자동 보존 ({directive}): {source} → {destination}");
                    return;

                case Materialization.CreateDirectory:
                    Directory.CreateDirectory(destination);
                    return;

                case Materialization.CreateParentDirectory:
                    var parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(parent) && IsInsideRoot(parent, destinationRoot))
                        Directory.CreateDirectory(parent);
                    return;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"PHP 소유 경로 자동 보존 실패 ({directive}): {ex.Message}");
        }
    }

    private static void CopyDirectoryMissing(string source, string destination)
    {
        const int maxFiles = 4000;
        const long maxBytes = 256L * 1024L * 1024L;
        var fileCount = 0;
        long totalBytes = 0;

        Directory.CreateDirectory(destination);
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((source, destination));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current.Source))
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var nextDestination = Path.Combine(current.Destination, info.Name);
                Directory.CreateDirectory(nextDestination);
                pending.Push((directory, nextDestination));
            }

            foreach (var file in Directory.EnumerateFiles(current.Source))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                fileCount++;
                totalBytes += info.Length;
                if (fileCount > maxFiles || totalBytes > maxBytes)
                    throw new InvalidDataException("참조 디렉터리 자동 보존 한도(4000 files / 256 MiB)를 초과했습니다.");

                var target = Path.Combine(current.Destination, info.Name);
                if (!File.Exists(target)) File.Copy(file, target, overwrite: false);
            }
        }
    }

    private static bool TryParseDirective(string line, out ParsedDirective parsed)
    {
        parsed = default;
        var trimmedStart = line.TrimStart();
        if (trimmedStart.Length == 0 || trimmedStart.StartsWith(';') || trimmedStart.StartsWith('#')) return false;
        var equals = line.IndexOf('=');
        if (equals <= 0) return false;

        var name = line[..equals].Trim();
        if (name.Length == 0) return false;
        var right = line[(equals + 1)..];
        var leadingCount = right.Length - right.TrimStart().Length;
        var leading = right[..leadingCount];
        var body = right[leadingCount..];
        if (body.Length == 0) return false;

        if (body[0] == '"' || body[0] == '\'')
        {
            var quote = body[0];
            var close = body.LastIndexOf(quote);
            if (close <= 0) return false;
            parsed = new ParsedDirective(name, body[1..close], line[..(equals + 1)] + leading + quote, quote + body[(close + 1)..]);
            return true;
        }

        var value = body.TrimEnd();
        var trailing = body[value.Length..];
        parsed = new ParsedDirective(name, value, line[..(equals + 1)] + leading, trailing);
        return true;
    }

    private static bool IsDriveRootRelative(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var normalized = value.Replace('/', '\\');
        return normalized.StartsWith('\\') && !normalized.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static bool BuildMapped(
        string relative,
        string sourceRoot,
        string destinationRoot,
        out string source,
        out string destination)
    {
        source = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
        return IsInsideRoot(source, sourceRoot) && IsInsideRoot(destination, destinationRoot);
    }

    private static bool TryRelativeInside(string path, string root, out string relative)
    {
        relative = string.Empty;
        if (!IsInsideRoot(path, root)) return false;
        relative = Path.GetRelativePath(root, path);
        if (relative == ".") relative = string.Empty;
        return true;
    }

    private static bool IsInsideRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private readonly record struct ParsedDirective(string Name, string Value, string Prefix, string Suffix)
    {
        public string Rebuild(string value) => Prefix + value + Suffix;
    }
}
