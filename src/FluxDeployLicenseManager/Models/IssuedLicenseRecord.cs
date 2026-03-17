namespace FluxDeployLicenseManager.Models;

public class IssuedLicenseRecord
{
    public string LicenseId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string LicenseType { get; set; } = "time";
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? DurationDays { get; set; }
    public int MaxRelaySites { get; set; }
    public int MaxRecipes { get; set; }
    public string[]? Features { get; set; }
    public string KeyData { get; set; } = "";
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LicenseStore
{
    public int NextId { get; set; }
    public List<IssuedLicenseRecord> Licenses { get; set; } = [];
}
