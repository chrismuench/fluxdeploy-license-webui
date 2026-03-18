using Microsoft.JSInterop;

namespace FluxDeployLicenseManager.Services;

/// <summary>
/// ECDSA P-256 SHA-256 signing/verification via browser Web Crypto API.
/// Compatible with .NET's ECDsa.SignData(data, SHA256, IeeeP1363FixedFieldConcatenation).
/// Keys are imported once and cached in JS. PEM only crosses interop on import.
/// </summary>
public class CryptoService
{
    private readonly IJSRuntime _js;

    public CryptoService(IJSRuntime js) => _js = js;

    public async Task ImportPrivateKeyAsync(string privateKeyPem)
    {
        await _js.InvokeAsync<bool>("ecdsaCrypto.importPrivateKey", privateKeyPem);
    }

    public async Task ImportPublicKeyAsync(string publicKeyPem)
    {
        await _js.InvokeAsync<bool>("ecdsaCrypto.importPublicKey", publicKeyPem);
    }

    public async Task<byte[]> SignAsync(byte[] data)
    {
        var dataBase64 = Convert.ToBase64String(data);
        var sigBase64 = await _js.InvokeAsync<string>("ecdsaCrypto.sign", dataBase64);
        return Convert.FromBase64String(sigBase64);
    }

    public async Task<bool> VerifyAsync(byte[] data, byte[] signature)
    {
        var dataBase64 = Convert.ToBase64String(data);
        var sigBase64 = Convert.ToBase64String(signature);
        return await _js.InvokeAsync<bool>("ecdsaCrypto.verify", dataBase64, sigBase64);
    }

    public async Task<(string PrivateKeyPem, string PublicKeyPem)> GenerateKeyPairAsync()
    {
        var result = await _js.InvokeAsync<KeyPairResult>("ecdsaCrypto.generateKeyPair");
        return (result.PrivateKeyPem, result.PublicKeyPem);
    }

    private class KeyPairResult
    {
        public string PrivateKeyPem { get; set; } = "";
        public string PublicKeyPem { get; set; } = "";
    }
}
