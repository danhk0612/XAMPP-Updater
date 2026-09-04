using System.IO;

namespace XamppUpdater.App;

internal sealed record PhpMyAdminBackupResult(string SourcePath, string BackupPath, string? Version, int Files, long Bytes);

internal sealed class PhpMyAdminBackupService
{
    public PhpMyAdminBackupResult Create(string xamppRoot)
    {
        var root = Path.GetFullPath(xamppRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        var source = Directory.EnumerateDirectories(root)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "phpMyAdmin", StringComparison.OrdinalIgnoreCase));
        if (source is null) throw new InvalidOperationException("현재 XAMPP에 phpMyAdmin 폴더가 없습니다.");

        var version = PhpMyAdminUpdateService.TryReadInstalledVersion(source);
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XAMPP-Updater", "PhpMyAdmin", "Backups");
        Directory.CreateDirectory(backupRoot);

        var safeVersion = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        var destination = Path.Combine(backupRoot, $"phpMyAdmin-manual-{safeVersion}-{DateTime.Now:yyyyMMdd-HHmmss}");

        var files = 0;
        long bytes = 0;
        CopyDirectory(source, destination, ref files, ref bytes);

        var backupVersion = PhpMyAdminUpdateService.TryReadInstalledVersion(destination);
        if (!string.IsNullOrWhiteSpace(version) && !string.Equals(version, backupVersion, StringComparison.OrdinalIgnoreCase))
        {
            try { Directory.Delete(destination, true); } catch { }
            throw new InvalidDataException("phpMyAdmin 백업 버전 검증에 실패했습니다.");
        }

        return new PhpMyAdminBackupResult(source, destination, version, files, bytes);
    }

    private static void CopyDirectory(string source, string destination, ref int files, ref long bytes)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var info = new FileInfo(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            files++;
            bytes += info.Length;
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), ref files, ref bytes);
        }
    }
}
