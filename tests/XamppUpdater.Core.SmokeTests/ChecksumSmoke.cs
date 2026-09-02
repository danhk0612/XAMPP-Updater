using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class ChecksumSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        const string page = "<a href=\"sha256sums.txt\">SHA256</a><a href=\"sha256sums.txt.asc\">PGP</a>";
        var url = PackagePreparationService.ResolveSha256ManifestUrl(
            "https://dlm.mariadb.com/browse/mariadb_server/10.4.34/winx64-packages/",
            page);
        if (!string.Equals(
                url,
                "https://dlm.mariadb.com/browse/mariadb_server/10.4.34/winx64-packages/sha256sums.txt",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SHA256 manifest URL resolver failed: {url ?? "<null>"}");
        }

        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var parsed = PackagePreparationService.ParseSha256Sum(
            $"{hash}  mariadb-10.4.34-winx64.zip\n{new string('a', 64)}  other.zip\n",
            "mariadb-10.4.34-winx64.zip");
        if (!string.Equals(parsed, hash.ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SHA256 manifest parser failed.");
        }
    }
}
