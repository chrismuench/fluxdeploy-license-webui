using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluxDeployLicenseManager.Models;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// Orchestrates license generation, verification, and storage.
/// Uses compact ECDSA P-256 format: 6-byte payload + 64-byte signature = 70 bytes,
/// encoded as 112 Crockford Base32 characters, formatted as XXXXX-XXXXX-...-XX.
/// Crypto uses browser SubtleCrypto via JS interop.
/// Storage uses GitHub API.
/// </summary>
public partial class LicenseService
{
    private readonly CryptoService _crypto;
    private readonly GitHubStorageService _github;

    private const int MaxOrgNameLength = 255;
    private const int PayloadLength = 6;
    private const int SignatureLength = 64; // ECDSA P-256 IEEE P1363
    private const int Base32Length = 112; // ceil(70 * 8 / 5)

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int FeatureAutopilot = 0;
    private const int FeatureMcc = 1;

    private static readonly DateTime Epoch = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public LicenseService(CryptoService crypto, GitHubStorageService github)
    {
        _crypto = crypto;
        _github = github;
    }

    private static string ValidateOrgName(string org)
    {
        if (string.IsNullOrWhiteSpace(org))
            throw new ArgumentException("Organization name is required.");

        org = org.Trim();
        if (org.Length > MaxOrgNameLength)
            throw new ArgumentException($"Organization name cannot exceed {MaxOrgNameLength} characters.");

        org = ControlCharsRegex().Replace(org, " ");
        return org;
    }

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharsRegex();

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

        var (store, success) = await _github.ReadStoreAsync();
        if (!success)
            throw new InvalidOperationException("Unable to read license data from GitHub. Please check your connection and try again.");

        store.NextId++;
        var sequenceId = (ushort)store.NextId;
        var licenseId = $"FD-{sequenceId:D5}";

        // Calculate expiry month offset from Jan 2024
        byte expiryMonth = 0; // 0 = perpetual
        DateTime? expiresAt = null;
        if (!string.Equals(type, "perpetual", StringComparison.OrdinalIgnoreCase))
        {
            expiresAt = DateTime.UtcNow.AddDays(durationDays);
            var monthsFromEpoch = ((expiresAt.Value.Year - Epoch.Year) * 12) + (expiresAt.Value.Month - Epoch.Month);
            expiryMonth = (byte)Math.Clamp(monthsFromEpoch, 1, 255);
        }

        // Build feature flags byte
        byte featureBits = 0;
        foreach (var f in features)
        {
            if (string.Equals(f, "autopilot", StringComparison.OrdinalIgnoreCase))
                featureBits |= (1 << FeatureAutopilot);
            else if (string.Equals(f, "mcc", StringComparison.OrdinalIgnoreCase))
                featureBits |= (1 << FeatureMcc);
        }

        // Flags byte: [version:2 MSB][features:6 LSB]
        byte flags = (byte)((0 << 6) | (featureBits & 0x3F));

        // Pack payload (6 bytes)
        var payload = new byte[PayloadLength];
        payload[0] = (byte)(sequenceId & 0xFF);
        payload[1] = (byte)(sequenceId >> 8);
        payload[2] = expiryMonth;
        payload[3] = (byte)Math.Clamp(maxRelays, 0, 255);
        payload[4] = (byte)Math.Clamp(maxRecipes, 0, 255);
        payload[5] = flags;

        // Sign with ECDSA P-256 via Web Crypto
        var signatureBytes = await _crypto.SignAsync(payload);

        // Combine payload + signature
        var keyBytes = new byte[PayloadLength + signatureBytes.Length];
        Array.Copy(payload, 0, keyBytes, 0, PayloadLength);
        Array.Copy(signatureBytes, 0, keyBytes, PayloadLength, signatureBytes.Length);

        // Encode as Crockford Base32 and format
        var encoded = Base32Encode(keyBytes);
        var key = FormatKey(encoded);

        var record = new IssuedLicenseRecord
        {
            LicenseId = licenseId,
            OrganizationName = org,
            LicenseType = type.ToLowerInvariant(),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            DurationDays = type.ToLowerInvariant() == "perpetual" ? null : durationDays,
            MaxRelaySites = maxRelays,
            MaxRecipes = maxRecipes,
            Features = features.Length > 0 ? features : null,
            KeyHash = ComputeKeyHash(key),
            LicenseKey = key,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        store.Licenses.Add(record);

        var safeOrg = org.Length > 50 ? org[..50] + "..." : org;
        await _github.WriteStoreAsync(store, $"Issue license {licenseId} to {safeOrg}");

        return (key, licenseId);
    }

    public async Task<(bool Valid, string? Error, LicensePayload? Payload, bool Expired)> VerifyLicenseKeyAsync(
        string key)
    {
        var cleaned = key.Replace("-", "").Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim().ToUpperInvariant();

        if (cleaned.Length != Base32Length)
            return (false, $"Invalid key length: expected {Base32Length} characters, got {cleaned.Length}", null, false);

        byte[] keyBytes;
        try
        {
            keyBytes = Base32Decode(cleaned);
        }
        catch
        {
            return (false, "Invalid key: contains invalid characters", null, false);
        }

        if (keyBytes.Length < PayloadLength + SignatureLength)
            return (false, "Invalid key format", null, false);

        var payload = keyBytes[..PayloadLength];
        var signature = keyBytes[PayloadLength..(PayloadLength + SignatureLength)];

        try
        {
            var valid = await _crypto.VerifyAsync(payload, signature);
            if (!valid)
                return (false, "Invalid license key", null, false);
        }
        catch
        {
            return (false, "Unable to verify the key. Please check the format and try again.", null, false);
        }

        // Decode payload
        var sequenceId = (ushort)(payload[0] | (payload[1] << 8));
        var expiryMonth = payload[2];
        var maxRelays = (int)payload[3];
        var maxRecipes = (int)payload[4];
        var flags = payload[5];

        var version = (flags >> 6) & 0x03;
        var featureBits = flags & 0x3F;

        if (version != 0)
            return (false, $"Unsupported key version: {version}", null, false);

        // Decode expiry
        DateTime? expiresAt = null;
        var isPerpetual = expiryMonth == 0;
        if (!isPerpetual)
        {
            expiresAt = Epoch.AddMonths(expiryMonth);
        }

        // Decode features
        var features = new List<string>();
        if ((featureBits & (1 << FeatureAutopilot)) != 0) features.Add("autopilot");
        if ((featureBits & (1 << FeatureMcc)) != 0) features.Add("mcc");

        var expired = !isPerpetual && expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow;

        var result = new LicensePayload
        {
            LicenseId = $"FD-{sequenceId:D5}",
            SequenceId = sequenceId,
            Type = isPerpetual ? "perpetual" : "time",
            ExpiresAt = expiresAt,
            MaxRelays = maxRelays,
            MaxRecipes = maxRecipes,
            Features = features.ToArray(),
            Version = version
        };

        return (true, null, result, expired);
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

    // ─── Base32 helpers ───

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string encoded)
    {
        int buffer = 0, bitsLeft = 0;
        var result = new List<byte>();
        foreach (var c in encoded)
        {
            var val = Alphabet.IndexOf(c);
            if (val < 0) throw new FormatException($"Invalid Base32 character: {c}");
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result.Add((byte)(buffer >> bitsLeft));
            }
        }
        return result.ToArray();
    }

    private static string FormatKey(string raw)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
                sb.Append('-');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }
}
