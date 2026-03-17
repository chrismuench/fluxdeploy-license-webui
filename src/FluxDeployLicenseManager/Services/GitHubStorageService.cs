using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluxDeployLicenseManager.Models;
using Microsoft.JSInterop;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// Reads/writes a JSON file in a GitHub repo via the GitHub Contents API.
/// Owner and repo are hardcoded in GitHubConfig.
/// </summary>
public class GitHubStorageService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private string? _currentSha;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GitHubStorageService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public GitHubConfig? Config { get; private set; }

    public bool IsConfigured => Config != null && !string.IsNullOrEmpty(Config.Token);

    private static string RepoPath => $"{GitHubConfig.Owner}/{GitHubConfig.Repo}";

    public async Task LoadConfigAsync()
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", "github_config");
        if (!string.IsNullOrEmpty(json))
            Config = JsonSerializer.Deserialize<GitHubConfig>(json, JsonOptions);
    }

    public async Task SaveConfigAsync(GitHubConfig config)
    {
        Config = config;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await _js.InvokeVoidAsync("localStorage.setItem", "github_config", json);
    }

    public async Task ClearConfigAsync()
    {
        Config = null;
        _currentSha = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", "github_config");
    }

    /// <summary>
    /// Verifies the PAT has access to the required repo and can write to it.
    /// Returns (success, errorMessage).
    /// </summary>
    public async Task<(bool Ok, string? Error)> ValidateAccessAsync()
    {
        if (Config == null) return (false, "Not configured");

        try
        {
            // Check the token can see the repo
            var repoRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{RepoPath}");
            AddHeaders(repoRequest);
            var repoResponse = await _http.SendAsync(repoRequest);

            if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, $"Repository '{RepoPath}' not found or token does not have access. Ensure the fine-grained PAT is scoped to this repo.");

            if (repoResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                repoResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Token does not have access to this repository. Ensure the fine-grained PAT has Contents read/write permission on the repo.");

            if (!repoResponse.IsSuccessStatusCode)
                return (false, $"Unexpected error: {repoResponse.StatusCode}");

            // Verify we can read/write by checking permissions
            var repoData = await repoResponse.Content.ReadFromJsonAsync<JsonElement>();
            var permissions = repoData.GetProperty("permissions");
            var canPush = permissions.GetProperty("push").GetBoolean();
            if (!canPush)
                return (false, "Token has read access but not write access. The fine-grained PAT needs Contents read/write permission.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<LicenseStore> ReadStoreAsync()
    {
        if (Config == null) return new LicenseStore();

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{RepoPath}/contents/{GitHubConfig.FilePath}");
            AddHeaders(request);
            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return new LicenseStore();

            var content = await response.Content.ReadFromJsonAsync<GitHubFileResponse>();
            if (content?.Content == null)
                return new LicenseStore();

            _currentSha = content.Sha;
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(
                content.Content.Replace("\n", "")));
            return JsonSerializer.Deserialize<LicenseStore>(json, JsonOptions) ?? new LicenseStore();
        }
        catch
        {
            return new LicenseStore();
        }
    }

    public async Task WriteStoreAsync(LicenseStore store, string commitMessage)
    {
        if (Config == null) throw new InvalidOperationException("GitHub not configured");

        var json = JsonSerializer.Serialize(store, JsonOptions);
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var body = new Dictionary<string, string>
        {
            ["message"] = commitMessage,
            ["content"] = contentBase64
        };
        if (_currentSha != null)
            body["sha"] = _currentSha;

        var request = new HttpRequestMessage(HttpMethod.Put,
            $"https://api.github.com/repos/{RepoPath}/contents/{GitHubConfig.FilePath}");
        AddHeaders(request);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error: {response.StatusCode} - {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<GitHubPutResponse>();
        _currentSha = result?.Content?.Sha;
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config!.Token);
        request.Headers.UserAgent.ParseAdd("FluxDeployLicenseManager/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
    }

    private class GitHubFileResponse
    {
        public string? Content { get; set; }
        public string? Sha { get; set; }
    }

    private class GitHubPutResponse
    {
        public GitHubPutContent? Content { get; set; }
    }

    private class GitHubPutContent
    {
        public string? Sha { get; set; }
    }
}
