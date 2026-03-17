using System.Text;
using System.Text.Json;
using FluxDeployLicenseManager.Models;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// Orchestrates license generation, verification, and storage.
/// Crypto uses browser SubtleCrypto via JS interop.
/// Storage uses GitHub API.
/// </summary>
public class LicenseService
{
    private readonly CryptoService _crypto;
    private readonly GitHubStorageService _github;

    public LicenseService(CryptoService crypto, GitHubStorageService github)
    {
        _crypto = crypto;
        _github = github;
    }

    public static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    public async Task<(string Key, string LicenseId)> GenerateLicenseAsync(
        string privateKeyPem, string org, string type, int durationDays,
        int maxRelays, int maxRecipes, string[] features, string? notes)
    {
        var store = await _github.ReadStoreAsync();
        store.NextId++;
        var licenseId = $"FD-{DateTime.UtcNow.Year}-{store.NextId:D5}";

        var payload = new LicensePayload
        {
            lid = licenseId,
            org = org,
            type = type.ToLowerInvariant(),
            issued = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            expires = type.ToLowerInvariant() == "perpetual"
                ? null
                : DateTime.UtcNow.AddDays(durationDays).ToString("yyyy-MM-dd"),
            maxRelays = maxRelays,
            maxRecipes = maxRecipes,
            features = features,
            ver = 1
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        var signatureBytes = await _crypto.SignAsync(privateKeyPem, payloadBytes);
        var key = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signatureBytes)}";

        var record = new IssuedLicenseRecord
        {
            LicenseId = licenseId,
            OrganizationName = org,
            LicenseType = type.ToLowerInvariant(),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = type.ToLowerInvariant() == "perpetual" ? null : DateTime.UtcNow.AddDays(durationDays),
            DurationDays = type.ToLowerInvariant() == "perpetual" ? null : durationDays,
            MaxRelaySites = maxRelays,
            MaxRecipes = maxRecipes,
            Features = features.Length > 0 ? features : null,
            KeyData = key,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        store.Licenses.Add(record);
        await _github.WriteStoreAsync(store, $"Issue license {licenseId} to {org}");

        return (key, licenseId);
    }

    public async Task<(bool Valid, string? Error, LicensePayload? Payload, bool Expired)> VerifyLicenseKeyAsync(
        string publicKeyPem, string key)
    {
        var cleaned = key.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
        var parts = cleaned.Split('.');
        if (parts.Length != 2)
            return (false, "Invalid key format - expected payload.signature", null, false);

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signatureBytes = Base64UrlDecode(parts[1]);

            var valid = await _crypto.VerifyAsync(publicKeyPem, payloadBytes, signatureBytes);
            if (!valid)
                return (false, "Signature verification failed", null, false);

            var payload = JsonSerializer.Deserialize<LicensePayload>(
                Encoding.UTF8.GetString(payloadBytes));

            var expired = false;
            if (payload?.expires != null && DateTime.TryParse(payload.expires, out var expiryDate))
                expired = expiryDate < DateTime.UtcNow;

            return (true, null, payload, expired);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null, false);
        }
    }

    public LicensePayload? DecodePayload(string key)
    {
        var cleaned = key.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
        var parts = cleaned.Split('.');
        if (parts.Length != 2) return null;

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            return JsonSerializer.Deserialize<LicensePayload>(
                Encoding.UTF8.GetString(payloadBytes));
        }
        catch { return null; }
    }

    public async Task<List<IssuedLicenseRecord>> GetAllLicensesAsync()
    {
        var store = await _github.ReadStoreAsync();
        return store.Licenses.OrderByDescending(l => l.CreatedAt).ToList();
    }

    public async Task UpdateNotesAsync(string licenseId, string? notes)
    {
        var store = await _github.ReadStoreAsync();
        var license = store.Licenses.FirstOrDefault(l => l.LicenseId == licenseId);
        if (license == null) return;

        license.Notes = notes;
        await _github.WriteStoreAsync(store, $"Update notes for {licenseId}");
    }

    public async Task DeleteLicenseAsync(string licenseId)
    {
        var store = await _github.ReadStoreAsync();
        store.Licenses.RemoveAll(l => l.LicenseId == licenseId);
        await _github.WriteStoreAsync(store, $"Delete license {licenseId}");
    }

    public async Task<(string PrivateKeyPem, string PublicKeyPem)> GenerateKeyPairAsync()
    {
        return await _crypto.GenerateKeyPairAsync();
    }
}
