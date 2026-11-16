using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace TickerQ.Tests.Licensing;

internal static class TestLicenseIssuer
{
    public const string CertificateFileName = "tickerq.tqlicense";
    public const string AuthorityFileName = "fixture-authority.json";
    public const string KeyId = "tickerq-desktop-test-fixture";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<string> ExportAsync(
        string parentDirectory, DateTimeOffset issuedAt, DateTimeOffset? expiresAt = null)
    {
        const int schemaVersion = 4;
        var runtimeExpiresAt = expiresAt ?? issuedAt.AddDays(30);
        if (runtimeExpiresAt <= issuedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiration must be later than issuance.");

        var licenseId = Guid.NewGuid();
        var algorithm = SignatureAlgorithm.Ed25519;
        using var signingKey = Key.Create(algorithm);
        // Evaluation terms are fixed at 30 days. A subscription-shaped test payload supports custom dates.
        var customTerm = expiresAt.HasValue;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion,
            keyId = KeyId,
            licenseId,
            workspaceId = Guid.NewGuid(),
            workspaceName = "TickerQ local test fixture",
            plan = 1, // Business
            licenseKind = customTerm ? 0 : 2, // Full or Evaluation
            issuedAt,
            runtimeExpiresAt,
            anchoredMinorLine = customTerm ? "5.x" : null,
        }, JsonOptions);
        var certificate = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion,
            algorithm = "Ed25519",
            keyId = KeyId,
            payload = EncodeBase64Url(payload),
            signature = EncodeBase64Url(algorithm.Sign(signingKey, payload)),
        }, JsonOptions);
        var authority = JsonSerializer.SerializeToUtf8Bytes(new TestLicenseAuthority(
            KeyId, signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)), JsonOptions);

        var directory = Directory.CreateDirectory(Path.Combine(parentDirectory, $"tickerq-test-{licenseId:N}"));
        var certificatePath = Path.Combine(directory.FullName, CertificateFileName);
        var authorityPath = Path.Combine(directory.FullName, AuthorityFileName);
        try
        {
            await File.WriteAllBytesAsync(authorityPath, authority).ConfigureAwait(false);
            await File.WriteAllBytesAsync(certificatePath, certificate).ConfigureAwait(false);
        }
        catch
        {
            File.Delete(certificatePath);
            File.Delete(authorityPath);
            directory.Delete();
            throw;
        }

        return directory.FullName;
    }

    public static TestLicenseAuthority ReadAuthority(string directory)
        => JsonSerializer.Deserialize<TestLicenseAuthority>(
            File.ReadAllBytes(Path.Combine(directory, AuthorityFileName)), JsonOptions)
            ?? throw new InvalidDataException("The test fixture authority is empty.");

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed record TestLicenseAuthority(string KeyId, byte[] PublicKey)
{
    public byte[]? Resolve(string keyId) => string.Equals(KeyId, keyId, StringComparison.Ordinal) ? PublicKey : null;
}
