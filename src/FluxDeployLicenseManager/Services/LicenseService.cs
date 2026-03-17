using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluxDeployLicenseManager.Models;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// Orchestrates license generation, verification, and storage.
/// Crypto uses browser SubtleCrypto via JS interop.
/// Storage uses GitHub API.
/// </summary>
public partial class LicenseService
{
    private readonly CryptoService _crypto;
    private readonly GitHubStorageService _github;

    // WEB-011: Max org name length
    private const int MaxOrgNameLength = 255;

    public LicenseService(CryptoService crypto, GitHubStorageService github)
    {
        _crypto = crypto;
        _github = github;
    }

    public static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // WEB-023: Explicit handling for all padding cases
    public static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 0: break;
            case 2: s += "=="; break;
            case 3: s += "="; break;
            default: throw new FormatException("Invalid Base64url string length");
        }
        return Convert.FromBase64String(s);
    }

    // WEB-011: Validate and sanitize org name
    private static string ValidateOrgName(string org)
    {
        if (string.IsNullOrWhiteSpace(org))
            throw new ArgumentException("Organization name is required.");

        org = org.Trim();
        if (org.Length > MaxOrgNameLength)
            throw new ArgumentException($"Organization name cannot exceed {MaxOrgNameLength} characters.");

        // WEB-020: Strip control characters that could be injected into git commit messages
        org = ControlCharsRegex().Replace(org, " ");

        return org;
    }

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharsRegex();

    // WEB-016: Compute SHA-256 hash of the key for storage (not the full key)
    private static string ComputeKeyHash(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<(string Key, string LicenseId)> GenerateLicenseAsync(
        string org, string type, int durationDays,
        int maxRelays, int maxRecipes, string[] features, string? notes)
    {
        org = ValidateOrgName(org);

        // WEB-013: Check read success before writing
        var (store, success) = await _github.ReadStoreAsync();
        if (!success)
            throw new InvalidOperationException("Unable to read license data from GitHub. Please check your connection and try again.");

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

        var signatureBytes = await _crypto.SignAsync(payloadBytes);
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
            // WEB-016: Store hash only, not the full key
            KeyHash = ComputeKeyHash(key),
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        store.Licenses.Add(record);

        // WEB-020: Sanitize org name in commit message (control chars already stripped)
        var safeOrg = org.Length > 50 ? org[..50] + "..." : org;
        await _github.WriteStoreAsync(store, $"Issue license {licenseId} to {safeOrg}");

        return (key, licenseId);
    }

    public async Task<(bool Valid, string? Error, LicensePayload? Payload, bool Expired)> VerifyLicenseKeyAsync(
        string key)
    {
        var cleaned = key.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
        var parts = cleaned.Split('.');
        if (parts.Length != 2)
            return (false, "Invalid key format - expected payload.signature", null, false);

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signatureBytes = Base64UrlDecode(parts[1]);

            var valid = await _crypto.VerifyAsync(payloadBytes, signatureBytes);
            if (!valid)
                return (false, "Signature verification failed", null, false);

            var payload = JsonSerializer.Deserialize<LicensePayload>(
                Encoding.UTF8.GetString(payloadBytes));

            var expired = false;
            if (payload?.expires != null && DateTime.TryParse(payload.expires, out var expiryDate))
                expired = expiryDate < DateTime.UtcNow;

            return (true, null, payload, expired);
        }
        catch (FormatException)
        {
            // WEB-008: User-friendly message for malformed keys
            return (false, "Invalid key format - the key appears to be corrupted or incomplete.", null, false);
        }
        catch
        {
            return (false, "Unable to verify the key. Please check the format and try again.", null, false);
        }
    }

    public async Task<List<IssuedLicenseRecord>> GetAllLicensesAsync()
    {
        var (store, _) = await _github.ReadStoreAsync();
        return store.Licenses.OrderByDescending(l => l.CreatedAt).ToList();
    }

    public async Task UpdateNotesAsync(string licenseId, string? notes)
    {
        var (store, success) = await _github.ReadStoreAsync();
        if (!success)
            throw new InvalidOperationException("Unable to read license data. Please try again.");

        var license = store.Licenses.FirstOrDefault(l => l.LicenseId == licenseId);
        if (license == null) return;

        license.Notes = notes;
        await _github.WriteStoreAsync(store, $"Update notes for {licenseId}");
    }

    public async Task<(string PrivateKeyPem, string PublicKeyPem)> GenerateKeyPairAsync()
    {
        return await _crypto.GenerateKeyPairAsync();
    }
}
