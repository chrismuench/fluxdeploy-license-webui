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

    // WEB-010: Serialize write operations to prevent TOCTOU race conditions
    private readonly SemaphoreSlim _writeLock = new(1, 1);

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

    // WEB-001: Use sessionStorage instead of localStorage (cleared when tab closes)
    public async Task LoadConfigAsync()
    {
        var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", "github_config");
        if (!string.IsNullOrEmpty(json))
            Config = JsonSerializer.Deserialize<GitHubConfig>(json, JsonOptions);
    }

    public async Task SaveConfigAsync(GitHubConfig config)
    {
        Config = config;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await _js.InvokeVoidAsync("sessionStorage.setItem", "github_config", json);
    }

    public async Task ClearConfigAsync()
    {
        Config = null;
        _currentSha = null;
        await _js.InvokeVoidAsync("sessionStorage.removeItem", "github_config");
    }

    /// <summary>
    /// Verifies the PAT has access to the required repo and can write to it.
    /// WEB-002: Also warns if the token is a classic PAT with overly broad scope.
    /// </summary>
    public async Task<(bool Ok, string? Error, string? Warning)> ValidateAccessAsync()
    {
        if (Config == null) return (false, "Not configured", null);

        try
        {
            var repoRequest = new HttpRequestMessage(HttpMethod.Get,
                $"repos/{RepoPath}");
            AddHeaders(repoRequest);
            var repoResponse = await _http.SendAsync(repoRequest);

            if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, "Repository not found or token does not have access. Ensure the fine-grained PAT is scoped to the correct repo.", null);

            if (repoResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                repoResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Token does not have access. Ensure the fine-grained PAT has Contents read/write permission.", null);

            if (!repoResponse.IsSuccessStatusCode)
                return (false, "Unable to connect to GitHub. Please try again.", null);

            var repoData = await repoResponse.Content.ReadFromJsonAsync<JsonElement>();
            var permissions = repoData.GetProperty("permissions");
            var canPush = permissions.GetProperty("push").GetBoolean();
            if (!canPush)
                return (false, "Token has read access but not write access. The fine-grained PAT needs Contents read/write permission.", null);

            // WEB-002: Warn if classic PAT detected (broad scope)
            string? warning = null;
            if (Config.Token.StartsWith("ghp_"))
                warning = "Classic PAT detected. For security, use a fine-grained token scoped to only this repository.";

            return (true, null, warning);
        }
        catch
        {
            return (false, "Unable to connect to GitHub. Check your network and try again.", null);
        }
    }

    // WEB-013: Distinguish between "file not found" (first run) and API errors
    public async Task<(LicenseStore Store, bool Success)> ReadStoreAsync()
    {
        if (Config == null) return (new LicenseStore(), false);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"repos/{RepoPath}/contents/{GitHubConfig.FilePath}");
            AddHeaders(request);
            var response = await _http.SendAsync(request);

            // 404 = file doesn't exist yet, expected on first run
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _currentSha = null;
                return (new LicenseStore(), true);
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"GitHub API read error: {response.StatusCode}");
                return (new LicenseStore(), false);
            }

            var content = await response.Content.ReadFromJsonAsync<GitHubFileResponse>();
            if (content?.Content == null)
                return (new LicenseStore(), false);

            _currentSha = content.Sha;
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(
                content.Content.Replace("\n", "")));
            var store = JsonSerializer.Deserialize<LicenseStore>(json, JsonOptions) ?? new LicenseStore();
            return (store, true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GitHub API read exception: {ex}");
            return (new LicenseStore(), false);
        }
    }

    // WEB-010: Write operations are serialized via _writeLock
    public async Task WriteStoreAsync(LicenseStore store, string commitMessage)
    {
        if (Config == null) throw new InvalidOperationException("GitHub not configured");

        await _writeLock.WaitAsync();
        try
        {
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
                $"repos/{RepoPath}/contents/{GitHubConfig.FilePath}");
            AddHeaders(request);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                // WEB-009: Don't expose raw GitHub API error body
                var statusCode = response.StatusCode;
                Console.Error.WriteLine($"GitHub API write error: {statusCode} - {await response.Content.ReadAsStringAsync()}");

                if (statusCode == System.Net.HttpStatusCode.Conflict)
                    throw new InvalidOperationException("Data was modified concurrently. Please refresh and try again.");

                throw new InvalidOperationException("Failed to save data to GitHub. Please try again.");
            }

            var result = await response.Content.ReadFromJsonAsync<GitHubPutResponse>();
            _currentSha = result?.Content?.Sha;
        }
        finally
        {
            _writeLock.Release();
        }
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
