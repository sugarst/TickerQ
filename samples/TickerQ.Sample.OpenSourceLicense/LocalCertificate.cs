using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using NSec.Cryptography;

internal static class LocalCertificate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static DateTimeOffset Verify(string certificatePath, string enrolledAuthorityPath, DateTimeOffset now)
    {
        var authority = Read<Authority>(File.ReadAllBytes(enrolledAuthorityPath));
        var envelope = Read<Envelope>(File.ReadAllBytes(certificatePath));
        if (envelope.SchemaVersion != 4 || envelope.Algorithm != "Ed25519" ||
            string.IsNullOrWhiteSpace(authority.KeyId) || envelope.KeyId != authority.KeyId)
            throw new InvalidDataException("The certificate does not match the enrolled desktop authority.");

        var key = PublicKey.Import(SignatureAlgorithm.Ed25519, authority.PublicKey, KeyBlobFormat.RawPublicKey);
        var payloadBytes = WebEncoders.Base64UrlDecode(envelope.Payload);
        var signature = WebEncoders.Base64UrlDecode(envelope.Signature);
        if (!SignatureAlgorithm.Ed25519.Verify(key, payloadBytes, signature))
            throw new InvalidDataException("The desktop certificate signature is invalid.");

        var payload = Read<Payload>(payloadBytes);
        if (payload.SchemaVersion != envelope.SchemaVersion || payload.KeyId != envelope.KeyId ||
            payload.LicenseId == Guid.Empty || payload.IssuedAt == default ||
            now < payload.IssuedAt || now >= payload.RuntimeExpiresAt)
            throw new InvalidDataException("The desktop certificate is invalid or outside its validity period.");

        return payload.RuntimeExpiresAt;
    }

    private static T Read<T>(byte[] data) => JsonSerializer.Deserialize<T>(data, JsonOptions)
        ?? throw new InvalidDataException("Empty certificate or authority file.");

    private sealed record Authority(string KeyId, byte[] PublicKey);
    private sealed record Envelope(int SchemaVersion, string Algorithm, string KeyId, string Payload, string Signature);
    private sealed record Payload(int SchemaVersion, string KeyId, Guid LicenseId, DateTimeOffset IssuedAt,
        DateTimeOffset RuntimeExpiresAt);
}
