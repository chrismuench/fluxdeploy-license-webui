namespace FluxDeployLicenseManager.Models;

/// <summary>
/// Decoded compact license key payload (6 bytes).
/// Matches the FluxDeploy server's LicenseService format.
/// </summary>
public class LicensePayload
{
    public string LicenseId { get; set; } = "";
    public ushort SequenceId { get; set; }
    public string Type { get; set; } = "time";
    public DateTime? ExpiresAt { get; set; }
    public int MaxRelays { get; set; }
    public int MaxRecipes { get; set; }
    public string[] Features { get; set; } = [];
    public int Version { get; set; }
}
