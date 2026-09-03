using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

public sealed record ApacheSslCompatibilityIssue(
    string CertificatePath,
    string? KeyPath,
    int? KeySize,
    bool SelfSigned,
    bool Repaired,
    string Message);

public static partial class ApacheSslCompatibilityService
{
    public static IReadOnlyList<ApacheSslCompatibilityIssue> InspectAndRepair(
        string apacheRoot,
        string confRoot,
        bool repairWeakSelfSigned)
    {
        var issues = new List<ApacheSslCompatibilityIssue>();
        if (!Directory.Exists(confRoot)) return issues;

        foreach (var conf in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(confRoot, path).Replace('\\', '/').StartsWith("original/", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(conf);
            var certMatches = CertificateRegex().Matches(text);
            if (certMatches.Count == 0) continue;

            var keyMatches = CertificateKeyRegex().Matches(text);
            for (var i = 0; i < certMatches.Count; i++)
            {
                var certPath = ResolveApachePath(apacheRoot, certMatches[i].Groups["path"].Value);
                var keyPath = keyMatches.Count == 0
                    ? null
                    : ResolveApachePath(apacheRoot, keyMatches[Math.Min(i, keyMatches.Count - 1)].Groups["path"].Value);

                if (!File.Exists(certPath)) continue;

                try
                {
                    using var certificate = X509Certificate2.CreateFromPemFile(certPath);
                    using var rsa = certificate.GetRSAPublicKey();
                    if (rsa is null) continue;

                    var keySize = rsa.KeySize;
                    if (keySize >= 2048) continue;

                    var selfSigned = string.Equals(
                        certificate.SubjectName.Name,
                        certificate.IssuerName.Name,
                        StringComparison.OrdinalIgnoreCase);

                    if (selfSigned && repairWeakSelfSigned && keyPath is not null)
                    {
                        RegenerateSelfSignedCertificate(certificate, certPath, keyPath);
                        issues.Add(new ApacheSslCompatibilityIssue(
                            certPath,
                            keyPath,
                            keySize,
                            true,
                            true,
                            $"약한 자체서명 SSL 인증서를 RSA 2048/SHA-256으로 자동 재생성: {Path.GetRelativePath(apacheRoot, certPath).Replace('\\', '/')} (기존 RSA {keySize})"));
                    }
                    else
                    {
                        issues.Add(new ApacheSslCompatibilityIssue(
                            certPath,
                            keyPath,
                            keySize,
                            selfSigned,
                            false,
                            selfSigned
                                ? $"자체서명 SSL 인증서의 RSA 키가 너무 작습니다: {Path.GetRelativePath(apacheRoot, certPath).Replace('\\', '/')} / RSA {keySize}"
                                : $"사용자/공인 SSL 인증서의 RSA 키가 너무 작아 자동 교체하지 않습니다: {Path.GetRelativePath(apacheRoot, certPath).Replace('\\', '/')} / RSA {keySize}"));
                    }
                }
                catch (CryptographicException ex)
                {
                    issues.Add(new ApacheSslCompatibilityIssue(
                        certPath,
                        keyPath,
                        null,
                        false,
                        false,
                        $"SSL 인증서를 해석하지 못했습니다: {Path.GetRelativePath(apacheRoot, certPath).Replace('\\', '/')} / {ex.Message}"));
                }
            }
        }

        return issues;
    }

    private static void RegenerateSelfSignedCertificate(
        X509Certificate2 oldCertificate,
        string certificatePath,
        string keyPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        var dnsName = oldCertificate.GetNameInfo(X509NameType.DnsName, false);
        if (string.IsNullOrWhiteSpace(dnsName)) dnsName = "localhost";

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={EscapeDistinguishedNameValue(dnsName)}"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        if (!dnsName.Equals("localhost", StringComparison.OrdinalIgnoreCase)) san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);
        using var generated = request.CreateSelfSigned(notBefore, notAfter);

        File.WriteAllText(certificatePath, generated.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
    }

    private static string ResolveApachePath(string apacheRoot, string configured)
    {
        var value = configured.Trim().Trim('"', '\'');
        value = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathFullyQualified(value)) return Path.GetFullPath(value);
        return Path.GetFullPath(Path.Combine(apacheRoot, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string EscapeDistinguishedNameValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace(",", "\\,", StringComparison.Ordinal)
             .Replace("+", "\\+", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("<", "\\<", StringComparison.Ordinal)
             .Replace(">", "\\>", StringComparison.Ordinal)
             .Replace(";", "\\;", StringComparison.Ordinal);

    [GeneratedRegex("(?im)^\\s*SSLCertificateFile\\s+[\\\"']?(?<path>[^\\\"'\\r\\n#]+)[\\\"']?\\s*(?:#.*)?$")]
    private static partial Regex CertificateRegex();

    [GeneratedRegex("(?im)^\\s*SSLCertificateKeyFile\\s+[\\\"']?(?<path>[^\\\"'\\r\\n#]+)[\\\"']?\\s*(?:#.*)?$")]
    private static partial Regex CertificateKeyRegex();
}
