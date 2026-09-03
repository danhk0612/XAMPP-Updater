using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public static class BackupIntegrityVerifier
{
    public static void Verify(BackupResult backup, bool requireLogicalBackup = false)
    {
        if (!File.Exists(backup.ManifestPath))
            throw new FileNotFoundException("백업 manifest를 찾을 수 없습니다.", backup.ManifestPath);

        var filesRoot = Path.Combine(backup.Manifest.BackupRoot, "files");
        if (!Directory.Exists(filesRoot))
            throw new DirectoryNotFoundException("백업 files 디렉터리가 없습니다: " + filesRoot);

        foreach (var item in backup.Manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(filesRoot, item.RelativePath));
            if (!IsUnderRoot(path, filesRoot) || !File.Exists(path))
                throw new InvalidDataException("백업 파일이 누락되었습니다: " + item.RelativePath);

            var info = new FileInfo(path);
            if (info.Length != item.Size)
                throw new InvalidDataException("백업 파일 크기가 manifest와 다릅니다: " + item.RelativePath);

            var sha = ComputeSha256(path);
            if (!string.Equals(sha, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("백업 파일 SHA256 검증 실패: " + item.RelativePath);
        }

        var logical = backup.Manifest.LogicalBackup;
        if (logical is null)
        {
            if (requireLogicalBackup)
                throw new InvalidDataException("필수 논리 백업 manifest가 없습니다.");
            return;
        }

        var logicalPath = Path.GetFullPath(Path.Combine(backup.Manifest.BackupRoot, logical.RelativePath));
        if (!IsUnderRoot(logicalPath, backup.Manifest.BackupRoot) || !File.Exists(logicalPath))
            throw new InvalidDataException("논리 백업 파일이 없습니다: " + logical.RelativePath);

        var logicalInfo = new FileInfo(logicalPath);
        if (logicalInfo.Length != logical.Size)
            throw new InvalidDataException("논리 백업 파일 크기가 manifest와 다릅니다: " + logical.RelativePath);

        var logicalSha = ComputeSha256(logicalPath);
        if (!string.Equals(logicalSha, logical.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("논리 백업 파일 SHA256 검증 실패: " + logical.RelativePath);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
