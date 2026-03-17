namespace FluxDeployLicenseManager.Models;

public class GitHubConfig
{
    public const string Owner = "chrismuench";
    public const string Repo = "fluxdeploy-license-data";
    public const string FilePath = "licenses.json";

    public string Token { get; set; } = "";
}
