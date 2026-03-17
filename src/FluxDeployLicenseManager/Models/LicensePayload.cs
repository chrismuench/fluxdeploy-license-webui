using System.Text.Json.Serialization;

namespace FluxDeployLicenseManager.Models;

public class LicensePayload
{
    public string lid { get; set; } = "";
    public string org { get; set; } = "";
    public string type { get; set; } = "time";
    public string issued { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? expires { get; set; }

    public int maxRelays { get; set; }
    public int maxRecipes { get; set; }
    public string[] features { get; set; } = [];
    public int ver { get; set; } = 1;
}
