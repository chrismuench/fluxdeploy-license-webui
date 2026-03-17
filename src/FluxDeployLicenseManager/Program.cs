using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FluxDeployLicenseManager;
using FluxDeployLicenseManager.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// WEB-015: Restrict HttpClient to GitHub API only
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri("https://api.github.com")
});
builder.Services.AddScoped<CryptoService>();
builder.Services.AddScoped<GitHubStorageService>();
builder.Services.AddScoped<LicenseService>();

await builder.Build().RunAsync();
