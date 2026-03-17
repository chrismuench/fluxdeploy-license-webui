namespace FluxDeployLicenseManager.Models;

public class GitHubConfig
{
    public const string RequiredRepo = "fluxdeploy-license-data";
    public const string FilePath = "licenses.json";

    public string Token { get; set; } = "";
    public string Owner { get; set; } = "";
}
