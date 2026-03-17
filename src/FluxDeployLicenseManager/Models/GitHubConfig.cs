namespace FluxDeployLicenseManager.Models;

public class GitHubConfig
{
    public string Token { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string FilePath { get; set; } = "licenses.json";
}
