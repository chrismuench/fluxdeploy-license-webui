using Microsoft.JSInterop;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// RSA PKCS1-v1_5 SHA-256 signing/verification via browser Web Crypto API.
/// Compatible with .NET's RSA.SignData(data, SHA256, Pkcs1) used by FluxDeploy Server.
/// WEB-006: Keys are imported once and cached in JS. PEM only crosses interop on import.
/// </summary>
public class CryptoService
{
    private readonly IJSRuntime _js;

    public CryptoService(IJSRuntime js) => _js = js;

    public async Task ImportPrivateKeyAsync(string privateKeyPem)
    {
        await _js.InvokeAsync<bool>("rsaCrypto.importPrivateKey", privateKeyPem);
    }

    public async Task ImportPublicKeyAsync(string publicKeyPem)
    {
        await _js.InvokeAsync<bool>("rsaCrypto.importPublicKey", publicKeyPem);
    }

    public async Task<byte[]> SignAsync(byte[] data)
    {
        var dataBase64 = Convert.ToBase64String(data);
        var sigBase64 = await _js.InvokeAsync<string>("rsaCrypto.sign", dataBase64);
        return Convert.FromBase64String(sigBase64);
    }

    public async Task<bool> VerifyAsync(byte[] data, byte[] signature)
    {
        var dataBase64 = Convert.ToBase64String(data);
        var sigBase64 = Convert.ToBase64String(signature);
        return await _js.InvokeAsync<bool>("rsaCrypto.verify", dataBase64, sigBase64);
    }

    public async Task<(string PrivateKeyPem, string PublicKeyPem)> GenerateKeyPairAsync()
    {
        var result = await _js.InvokeAsync<KeyPairResult>("rsaCrypto.generateKeyPair");
        return (result.PrivateKeyPem, result.PublicKeyPem);
    }

    private class KeyPairResult
    {
        public string PrivateKeyPem { get; set; } = "";
        public string PublicKeyPem { get; set; } = "";
    }
}
