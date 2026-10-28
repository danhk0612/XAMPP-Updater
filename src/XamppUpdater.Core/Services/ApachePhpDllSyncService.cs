using System.Security.Cryptography;
using System.Text.Json;

namespace XamppUpdater.Core.Services;

// PHP and Apache can load different copies of the same dependency DLL.
// Synchronize only DLLs reached from php_curl.dll that collide with apache/bin.
// Never replace Apache's OpenSSL/APR core libraries as a side effect of a PHP update.
public static class ApachePhpDllSyncService
{
    public sealed record Change(string Name, string Source, string Target, bool Existed,
        string? OriginalSha256, string AppliedSha256);

    public sealed record Journal(IReadOnlyList<JournalEntry> Files);
    public sealed record JournalEntry(string Name, bool Existed, string? OriginalSha256, string AppliedSha256);

    private const string JournalFile = "apache-php-dll-sync.json";
    private const string BackupFolder = "apache-php-dll-sync";

    public static IReadOnlyList<Change> Plan(
        string phpRoot,
        string apacheRoot,
        Func<string, IReadOnlyList<string>>? readImports = null)
    {
        var curl = Path.Combine(phpRoot, "ext", "php_curl.dll");
        if (!File.Exists(curl)) return Array.Empty<Change>();

        readImports ??= PeDependencyInspector.ReadImports;
        var apacheBin = Path.Combine(apacheRoot, "bin");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(curl);

        while (queue.Count > 0)
        {
            var file = queue.Dequeue();
            if (!seen.Add(Path.GetFullPath(file))) continue;
            foreach (var imported in readImports(file))
            {
                var name = Path.GetFileName(imported);
                if (!IsSafeDllName(name)) continue;
                var dependency = Path.Combine(phpRoot, name);
                if (!File.Exists(dependency)) continue;
                // Other PHP extensions are not part of cURL's dependency closure.
                if (IsApacheOwnedDependency(name)) continue;
                candidates.Add(name);
                queue.Enqueue(dependency);
            }
        }

        return candidates
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var source = Path.Combine(phpRoot, name);
                var target = Path.Combine(apacheBin, name);
                var sourceHash = Hash(source);
                var existed = File.Exists(target);
                return new Change(name, source, target, existed,
                    existed ? Hash(target) : null, sourceHash);
            })
            .Where(change => change.Existed &&
                !string.Equals(change.OriginalSha256, change.AppliedSha256, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static void Capture(IReadOnlyList<Change> changes, string backupRoot)
    {
        if (changes.Count == 0) return;
        var journalPath = GetJournalPath(backupRoot);
        if (File.Exists(journalPath))
            throw new InvalidOperationException("이 백업에는 Apache DLL 동기화 기록이 이미 있습니다. 새 백업을 생성하세요.");

        var backupDir = Path.Combine(backupRoot, BackupFolder);
        Directory.CreateDirectory(backupDir);
        foreach (var change in changes)
        {
            ValidateName(change.Name);
            if (change.Existed)
            {
                var snapshot = Path.Combine(backupDir, change.Name);
                File.Copy(change.Target, snapshot, overwrite: false);
                if (!string.Equals(Hash(snapshot), change.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Apache DLL 백업 SHA256 불일치: " + change.Name);
            }
        }

        var journal = new Journal(changes.Select(change =>
            new JournalEntry(change.Name, change.Existed, change.OriginalSha256, change.AppliedSha256)).ToArray());
        var temporary = journalPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
            File.Move(temporary, journalPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Apply(IReadOnlyList<Change> changes)
    {
        foreach (var change in changes)
        {
            if (!string.Equals(Hash(change.Source), change.AppliedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PHP 원본 DLL이 변경되었습니다: " + change.Name);
            AtomicCopy(change.Source, change.Target);
            if (!string.Equals(Hash(change.Target), change.AppliedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Apache DLL 동기화 SHA256 불일치: " + change.Name);
        }
    }

    public static IReadOnlyList<string> Restore(string backupRoot, string apacheRoot)
    {
        var journal = ReadJournal(backupRoot);
        if (journal is null) return Array.Empty<string>();
        var warnings = new List<string>();
        foreach (var item in journal.Files)
        {
            var destination = Path.Combine(apacheRoot, "bin", item.Name);
            var current = File.Exists(destination) ? Hash(destination) : null;
            if (string.Equals(current, item.OriginalSha256, StringComparison.OrdinalIgnoreCase)) continue;
            // A separate Apache update may have replaced this file since PHP was updated.
            // Do not silently overwrite changes made by another component's update.
            if (!string.Equals(current, item.AppliedSha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("별도 Apache 변경으로 DLL 복원을 건너뜀: " + item.Name);
                continue;
            }

            if (item.Existed)
                AtomicCopy(Path.Combine(backupRoot, BackupFolder, item.Name), destination);
            else
                File.Delete(destination);
            var restored = File.Exists(destination) ? Hash(destination) : null;
            if (!string.Equals(restored, item.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Apache DLL 복원 SHA256 불일치: " + item.Name);
        }
        return warnings;
    }

    public static void VerifyBackup(string backupRoot)
    {
        var journal = ReadJournal(backupRoot);
        if (journal is null) return;
        foreach (var item in journal.Files)
        {
            if (!item.Existed) continue;
            var file = Path.Combine(backupRoot, BackupFolder, item.Name);
            if (!File.Exists(file) ||
                !string.Equals(Hash(file), item.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Apache DLL 롤백 백업 손상: " + item.Name);
        }
    }

    public static IReadOnlyList<string> CaptureCurrent(string backupRoot, string apacheRoot, string safetyRoot)
    {
        var journal = ReadJournal(backupRoot);
        if (journal is null) return Array.Empty<string>();
        var present = new List<string>();
        foreach (var item in journal.Files)
        {
            var current = Path.Combine(apacheRoot, "bin", item.Name);
            if (!File.Exists(current)) continue;
            Directory.CreateDirectory(safetyRoot);
            File.Copy(current, Path.Combine(safetyRoot, item.Name), overwrite: true);
            present.Add(item.Name);
        }
        return present;
    }

    public static void RestoreCurrent(string backupRoot, string apacheRoot, string safetyRoot,
        IReadOnlyList<string> originallyPresent)
    {
        var journal = ReadJournal(backupRoot);
        if (journal is null) return;
        foreach (var item in journal.Files)
        {
            var destination = Path.Combine(apacheRoot, "bin", item.Name);
            if (originallyPresent.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                AtomicCopy(Path.Combine(safetyRoot, item.Name), destination);
            else if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    private static Journal? ReadJournal(string backupRoot)
    {
        var path = GetJournalPath(backupRoot);
        if (!File.Exists(path)) return null; // Existing backups have no DLL journal.
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Apache DLL 백업 기록이 비어 있습니다.");
        if (journal.Files is null || journal.Files.Select(item => item.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Files.Count)
            throw new InvalidDataException("Apache DLL 백업 기록이 잘못되었습니다.");
        foreach (var item in journal.Files)
        {
            ValidateName(item.Name);
            if (item.Existed && string.IsNullOrWhiteSpace(item.OriginalSha256))
                throw new InvalidDataException("Apache DLL 원본 해시가 없습니다: " + item.Name);
            if (string.IsNullOrWhiteSpace(item.AppliedSha256))
                throw new InvalidDataException("Apache DLL 적용 해시가 없습니다: " + item.Name);
        }
        return journal;
    }

    private static string GetJournalPath(string backupRoot) => Path.Combine(backupRoot, JournalFile);

    private static bool IsSafeDllName(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) &&
        !name.Contains(':') && !name.Contains('\\') && !name.Contains('/');

    private static void ValidateName(string name)
    {
        if (!IsSafeDllName(name) || name is "." or "..")
            throw new InvalidDataException("잘못된 DLL 백업 파일명: " + name);
    }

    private static bool IsApacheOwnedDependency(string name) =>
        name.StartsWith("libssl", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libcrypto", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libapr", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("php", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("libhttpd.dll", StringComparison.OrdinalIgnoreCase);

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AtomicCopy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".xampp-updater-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
