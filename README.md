# FluxDeploy License Manager

A browser-based tool for generating and managing FluxDeploy license keys. Built with Blazor WebAssembly and hosted on GitHub Pages — no server required.

**Live app:** https://chrismuench.github.io/fluxdeploy-license-webui/

## How It Works

- **RSA-4096 signing** runs entirely in the browser via the Web Crypto API
- **License records** are stored in a separate private GitHub repo via the GitHub Contents API
- **Private keys** are loaded from a `.pem` file each session and held in memory only — never persisted to the browser or sent anywhere
- **Generated keys** are compatible with FluxDeploy Server's `LicenseService`, which validates them offline using an embedded public key

## Setup

### 1. Create a private data repo

Create a private GitHub repo to store license records (e.g. `fluxdeploy-license-data`). This is where the app reads/writes `licenses.json`.

### 2. Generate a Personal Access Token

Go to [GitHub Settings > Fine-grained tokens](https://github.com/settings/personal-access-tokens/new):

- **Token name:** `fluxdeploy-license-webui`
- **Repository access:** Only select repositories > your data repo
- **Permissions > Repository permissions:** Contents > Read and write

### 3. Configure the app

Open the app and enter your GitHub PAT, repo owner, and repo name on the Setup page. This is stored in your browser's `localStorage` and persists across sessions.

### 4. Generate or load a key pair

Go to the **Keys** tab and either:

- **Generate a new RSA-4096 key pair** — both files download automatically
- **Load existing keys** from `.pem` files via the file picker

Store the private key securely. The public key (`fluxdeploy-license-public.pem`) needs to be embedded in FluxDeploy Server at `src/DeploymentSystem.Server/Resources/license-public.pem`.

## Features

| Tab | Description |
|-----|-------------|
| **Generate** | Create license keys with org name, type (time-based/perpetual), duration, relay/recipe limits, and feature flags |
| **Licenses** | View all issued licenses, copy keys, edit notes, delete records. Filter by status. |
| **Verify** | Paste any key to verify its signature and decode the payload |
| **Keys** | Load private/public keys, generate new key pairs, download public key |

## Architecture

```
Browser (Blazor WASM)
  ├── Web Crypto API (SubtleCrypto)     RSA-4096 PKCS1-v1_5 SHA-256 sign/verify
  ├── GitHub Contents API                Read/write licenses.json in private repo
  └── localStorage                       GitHub config (PAT, owner, repo)
```

The signing algorithm (`RSASSA-PKCS1-v1_5` with SHA-256) is identical to what .NET's `RSA.SignData(data, SHA256, Pkcs1)` produces, so keys generated here are fully compatible with FluxDeploy Server's verification.

## License Key Format

Keys are Base64url-encoded: `{payload}.{signature}`

**Payload** (JSON):
```json
{
  "lid": "FD-2026-00042",
  "org": "Contoso IT Services",
  "type": "time",
  "issued": "2026-03-17",
  "expires": "2027-03-17",
  "maxRelays": 10,
  "maxRecipes": 0,
  "features": ["pxe", "multicast", "autopilot"],
  "ver": 1
}
```

## Development

Requires .NET 10 SDK (preview).

```bash
cd src/FluxDeployLicenseManager
dotnet run
```

The app runs at `https://localhost:5000` in development. The GitHub Actions workflow handles the `<base href>` rewrite for the GitHub Pages subpath deployment.

## Deployment

Pushes to `main` automatically build and deploy to GitHub Pages via the `.github/workflows/deploy.yml` workflow.
