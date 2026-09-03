using System.Reflection.PortableExecutable;
using System.Text;

namespace XamppUpdater.Core.Services;

public sealed record PeMissingDependency(string BinaryPath, string DependencyName);

public static class PeDependencyInspector
{
    public static IReadOnlyList<string> ReadImports(string binaryPath)
    {
        if (!File.Exists(binaryPath)) return Array.Empty<string>();
        try
        {
            using var stream = File.OpenRead(binaryPath);
            using var reader = new PEReader(stream);
            var headers = reader.PEHeaders;
            var directory = headers.PEHeader?.ImportTableDirectory ?? default;
            if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
                return Array.Empty<string>();

            var image = reader.GetEntireImage().GetReader();
            var descriptorOffset = RvaToOffset(headers, directory.RelativeVirtualAddress);
            if (descriptorOffset < 0) return Array.Empty<string>();

            var result = new List<string>();
            var offset = descriptorOffset;
            while (offset + 20 <= image.Length)
            {
                var descriptor = image;
                descriptor.Offset = offset;
                var originalFirstThunk = descriptor.ReadUInt32();
                var timeDateStamp = descriptor.ReadUInt32();
                var forwarderChain = descriptor.ReadUInt32();
                var nameRva = descriptor.ReadUInt32();
                var firstThunk = descriptor.ReadUInt32();
                if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0 && nameRva == 0 && firstThunk == 0)
                    break;

                var nameOffset = RvaToOffset(headers, unchecked((int)nameRva));
                if (nameOffset >= 0)
                {
                    var name = ReadAsciiZ(image, nameOffset);
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                }
                offset += 20;
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<PeMissingDependency> FindMissingDependencies(
        string binaryPath,
        IEnumerable<string> searchDirectories,
        int maxDepth = 4)
    {
        var directories = searchDirectories
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<PeMissingDependency>();
        Walk(Path.GetFullPath(binaryPath), 0);
        return missing
            .DistinctBy(item => item.BinaryPath + "|" + item.DependencyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void Walk(string path, int depth)
        {
            if (depth > maxDepth || !File.Exists(path) || !visited.Add(path)) return;
            foreach (var dependency in ReadImports(path))
            {
                if (IsApiSet(dependency)) continue;
                var resolved = ResolveDependency(dependency, Path.GetDirectoryName(path), directories);
                if (resolved is null)
                {
                    missing.Add(new PeMissingDependency(path, dependency));
                    continue;
                }
                if (depth < maxDepth && !IsWindowsSystemPath(resolved)) Walk(resolved, depth + 1);
            }
        }
    }

    public static string? FindAnywhere(string root, string fileName)
    {
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(fileName)) return null;
        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDependency(string dependency, string? binaryDirectory, IReadOnlyList<string> directories)
    {
        if (!string.IsNullOrWhiteSpace(binaryDirectory))
        {
            var local = Path.Combine(binaryDirectory, dependency);
            if (File.Exists(local)) return local;
        }
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, dependency);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsApiSet(string name) =>
        name.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsSystemPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windows) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(windows), StringComparison.OrdinalIgnoreCase);
    }

    private static int RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            var start = section.VirtualAddress;
            var size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= start && rva < start + size)
                return section.PointerToRawData + (rva - start);
        }
        return -1;
    }

    private static string ReadAsciiZ(System.Reflection.Metadata.BlobReader image, int offset)
    {
        if (offset < 0 || offset >= image.Length) return string.Empty;
        image.Offset = offset;
        var bytes = new List<byte>();
        while (image.Offset < image.Length)
        {
            var value = image.ReadByte();
            if (value == 0) break;
            bytes.Add(value);
            if (bytes.Count > 1024) break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}
