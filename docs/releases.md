# Release artifacts and semantic versioning

GitHub releases are produced by [`.github/workflows/release.yml`](../.github/workflows/release.yml).

## Version source

The release tag is the single version source and must use:

```text
vX.X.X
```

Examples:

- Valid: `v1.0.0`, `v2.14.3`
- Invalid: `1.0.0`, `v1.0`, `v1.0.0-beta`

For tag `v1.2.3`, every component uses:

| Metadata | Value |
|---|---|
| Product/package version | `1.2.3` |
| Informational version | `1.2.3` |
| CLR assembly version | `1.2.3.0` |
| File version | `1.2.3.0` |

The workflow reads the built managed assembly and fails before packaging if these values do not match.

## Artifact matrix

| Component | Platform | Architecture | Archive |
|---|---|---|---|
| MCP server | Windows | x64 | `desktop-computer-use-windows-x64-vX.X.X.zip` |
| MCP server | Windows | ARM64 | `desktop-computer-use-windows-arm64-vX.X.X.zip` |
| MCP discovery companion | Linux | x64 | `desktop-computer-use-linux-x64-vX.X.X.tar.gz` |
| MCP discovery companion | Linux | ARM64 | `desktop-computer-use-linux-arm64-vX.X.X.tar.gz` |
| Profile builder | Windows | x64 | `desktop-computer-use-profile-builder-windows-x64-vX.X.X.zip` |
| Profile builder | Windows | ARM64 | `desktop-computer-use-profile-builder-windows-arm64-vX.X.X.zip` |

The Windows server contains full FlaUI automation. The Linux server preserves MCP discovery and profile compatibility but returns `PlatformNotSupported` for native Windows actions.

The profile builder is Windows-only because live snapshots require FlaUI and an interactive Windows desktop.

## Archive contents

Every archive includes:

- The self-contained executable
- `README.md`
- `profiles/example.application.json`
- `VERSION`
- `release-manifest.json`

Server archives also include:

- `appsettings.json`

Example `VERSION`:

```text
1.2.3
```

Example `release-manifest.json`:

```json
{
  "tag": "v1.2.3",
  "version": "1.2.3",
  "assemblyVersion": "1.2.3.0",
  "component": "profile-builder",
  "runtimeIdentifier": "win-x64",
  "commit": "0123456789abcdef0123456789abcdef01234567",
  "authenticodeSigned": true,
  "signingProvider": "Azure Artifact Signing"
}
```

Linux manifests always report `authenticodeSigned: false`. During Azure Artifact Signing onboarding, Windows manifests also report `false`; after the production certificate profile is enabled they report `true`.

See [Azure Artifact Signing setup](artifact-signing.md).

## Choose the correct architecture

Windows PowerShell:

```powershell
$env:PROCESSOR_ARCHITECTURE
```

- `AMD64`: use `windows-x64`
- `ARM64`: use `windows-arm64`

Linux:

```bash
uname -m
```

- `x86_64`: use `linux-x64`
- `aarch64` or `arm64`: use `linux-arm64`

## Verify checksums

Each release includes `SHA256SUMS.txt`.

Linux:

```bash
sha256sum -c SHA256SUMS.txt
```

Windows PowerShell:

```powershell
Get-FileHash .\desktop-computer-use-windows-x64-v1.2.3.zip -Algorithm SHA256
```

Compare the result with the corresponding line in `SHA256SUMS.txt`.

## Run self-contained executables directly

Do not prefix release executables with `dotnet`.

Correct:

```powershell
.\desktop-computer-use.exe
.\desktop-computer-use-profile-builder.exe help
```

Incorrect:

```powershell
dotnet .\desktop-computer-use.exe
```

The latter treats the native self-contained app host as a framework-dependent assembly and can report missing `hostpolicy.dll` or `.runtimeconfig.json`.

## Create a release

Push a strict semantic-version tag:

```bash
git tag v1.2.3
git push origin v1.2.3
```

Alternatively, run the **Release executables** workflow manually and enter `v1.2.3`.

The workflow:

1. Validates the tag.
2. Builds and tests the solution.
3. Publishes all six self-contained artifacts with identical semantic versions.
4. Verifies assembly metadata.
5. Writes package manifests.
6. Generates SHA-256 checksums.
7. Creates or updates the GitHub release.
