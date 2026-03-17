using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FluxDeployLicenseManager;
using FluxDeployLicenseManager.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<CryptoService>();
builder.Services.AddScoped<GitHubStorageService>();
builder.Services.AddScoped<LicenseService>();

await builder.Build().RunAsync();
