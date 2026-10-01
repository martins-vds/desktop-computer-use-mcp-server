# Desktop Computer Use MCP Server

An open-source Model Context Protocol server for operating allowlisted Windows desktop applications through Microsoft UI Automation.

The native automation implementation uses:

- [.NET 8](https://dotnet.microsoft.com/)
- The official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [FlaUI](https://github.com/FlaUI/FlaUI) with configurable UIA2 and UIA3 backends
- Local stdio transport

Release archives are produced for Windows and Linux on both x64 and ARM64. The Windows executable contains the full FlaUI automation implementation. The Linux executable exposes the same MCP discovery surface and can list profiles, but native Windows Forms actions return an explicit `PlatformNotSupported` result.

## Status

The server implements:

- Allowlisted application profiles
- Verified launch and process attachment
- Serialized UI Automation on a dedicated MTA thread
- UIA2 and UIA3 backends
- Bounded application-window inspection
- Stable semantic and explicit selectors
- Invoke, value, selection, expand/collapse, scroll, and state-wait actions
- Post-action observation
- Sensitive-value redaction
- Optional application-window-only capture
- Structured MCP results and typed failures
- Audit logging to stderr
- Versioned application profiles with legacy compatibility
- Rich UIA snapshots with view signatures, relationships, and nearby labels
- Deterministic fuzzy intent ranking with score evidence
- Shadow-mode profile healing proposals that never write files

The repository also includes a profile builder and an optional GitHub Copilot SDK adapter. The desktop automation server itself does not depend on Copilot.

It intentionally does not expose arbitrary shell commands, PowerShell, executable paths, desktop-wide inspection, coordinate clicking, global typing, or clipboard access.

Image and OCR automation are deferred until qualification against a real application demonstrates that semantic UI Automation is insufficient.

## Documentation

- [Profile builder guide](docs/profile-builder.md)
- [Release artifacts and versioning](docs/releases.md)
- [Azure Artifact Signing setup](docs/artifact-signing.md)
- [Quality analysis](artifacts/quality/QUALITY-ANALYSIS.md)

## Requirements

Runtime requirements:

- Windows 10 or Windows 11
- An interactive signed-in Windows user session
- The MCP server and target application running as the same user and at compatible integrity levels

Downloaded release executables are self-contained and do not require a separate .NET runtime. Running framework-dependent build output requires the .NET 8 runtime.

Development requirements:

- .NET 8 SDK
- A Windows host for meaningful UI Automation integration testing

The solution can be cross-built on Linux, but FlaUI actions cannot execute there. The test WinForms fixture compiles as a no-op stub on non-Windows hosts and as the real WinForms application on Windows.

## Build

```powershell
dotnet restore DesktopComputerUse.sln
dotnet build DesktopComputerUse.sln --no-restore
```

Publish the same self-contained executables produced by the release workflow:

```powershell
dotnet publish src/DesktopComputerUse.Server/DesktopComputerUse.Server.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=1.2.3 `
  -p:AssemblyVersion=1.2.3.0 `
  -p:FileVersion=1.2.3.0 `
  -p:InformationalVersion=1.2.3
```

```bash
dotnet publish src/DesktopComputerUse.Server.Linux/DesktopComputerUse.Server.Linux.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version=1.2.3 \
  -p:AssemblyVersion=1.2.3.0 \
  -p:FileVersion=1.2.3.0 \
  -p:InformationalVersion=1.2.3
```

Run portable tests:

```powershell
dotnet test tests/DesktopComputerUse.Automation.Tests/DesktopComputerUse.Automation.Tests.csproj
dotnet test tests/DesktopComputerUse.Server.Tests/DesktopComputerUse.Server.Tests.csproj
```

On an interactive Windows host, build the WinForms fixture and run the integration tests:

```powershell
dotnet build tests/DesktopComputerUse.TestApp/DesktopComputerUse.TestApp.csproj
dotnet test tests/DesktopComputerUse.Automation.Tests/DesktopComputerUse.Automation.Tests.csproj
```

The Windows integration test exits without failure when the fixture executable is unavailable. Check the test output to confirm that it actually ran when qualifying a Windows environment.

## Application profiles

Every launched or attached application must match a JSON profile. The server loads files named `*.application.json` from the configured profile directory.

Set an absolute profile directory through:

```powershell
$env:DESKTOP_COMPUTER_USE_PROFILES = "C:\automation\profiles"
```

`DesktopComputerUse__ProfilesDirectory` follows normal .NET configuration conventions and is also supported. If neither variable is set, the server uses `profiles` relative to its working directory.

An example profile is available at [`profiles/example.application.json`](profiles/example.application.json):

```json
{
  "schemaVersion": 2,
  "id": "example-app",
  "displayName": "Example WinForms App",
  "executablePath": "C:/Program Files/Example/Example.exe",
  "backend": "uia3",
  "mainWindow": {
    "titleRegex": "^Example"
  },
  "operationTimeoutMs": 10000,
  "pollIntervalMs": 100,
  "maxTreeDepth": 6,
  "maxResults": 300,
  "enableScreenshots": false,
  "semanticTargets": {
    "save-record": {
      "intent": "Save the current record",
      "synonyms": ["Save", "Apply", "Commit changes"],
      "expectedControlTypes": ["Button", "MenuItem"],
      "requiredPatterns": ["Invoke"],
      "strategies": [
        {
          "automationId": "SaveButton",
          "controlType": "Button",
          "weight": 1.0
        },
        {
          "name": "Save",
          "controlType": "Button",
          "weight": 0.8
        }
      ],
      "thresholds": {
        "minimumConfidence": 0.85,
        "minimumMargin": 0.12
      },
      "allowAiAssistance": false
    }
  },
  "sensitiveAutomationIds": [
    "PasswordTextBox"
  ]
}
```

Relative executable and working-directory paths are resolved from the profile file's directory. The executable is allowed to be absent when profiles are loaded so profiles can be deployed before applications, but launch fails explicitly until the file exists.

Version 1 profiles using `semanticSelectors` remain supported. At runtime they are adapted to one exact version 2 strategy.

### Fuzzy semantic targets

The resolver uses this order:

1. Exact configured strategies, ordered by weight.
2. Required control type, UIA pattern, view, and ancestor gates.
3. Deterministic text, nearby-label, help-text, structure, and fingerprint scoring.
4. An ambiguity result when the best score or margin is insufficient.

Fuzzy resolution is exposed diagnostically through `resolve_control_intent`. State-changing tools continue to use validated configured strategies; they do not automatically act on a fuzzy candidate.

Candidate IDs such as `node-0017` are valid only inside one application snapshot. Profiles store selectors and fingerprints, never snapshot-local candidate IDs.

### Profile builder

Build and run:

```powershell
dotnet build src/DesktopComputerUse.ProfileBuilder/DesktopComputerUse.ProfileBuilder.csproj
dotnet run --project src/DesktopComputerUse.ProfileBuilder -- help
```

Downloaded releases contain a self-contained Windows executable, so .NET does not need to be installed:

```powershell
& ".\desktop-computer-use-profile-builder.exe" help
```

The full profile builder is published for Windows x64 and Windows ARM64 only because live `snapshot` discovery depends on FlaUI and an interactive Windows desktop. Offline Linux builder artifacts are not published.

See the [profile builder guide](docs/profile-builder.md) for installation, a minimal bootstrap profile, and complete command examples.

Commands:

```text
validate <profile>
snapshot <profile> <output.json> [--attach <pid>]
search <profile> <snapshot.json> <semantic-key>
add-target <profile> <snapshot.json> <semantic-key> <intent> <candidate-id> <output-profile> [--pattern <pattern>]
diff <original-profile> <draft-profile>
apply <draft-profile> <target-profile>
```

Recommended authoring sequence:

1. Start with a minimal allowlisted profile.
2. Capture a snapshot on Windows.
3. Inspect or search candidates.
4. Add one reviewed target at a time.
5. Review the profile diff.
6. Apply the validated draft explicitly.

The optional Copilot command is intentionally in a separate project:

```powershell
dotnet run --project src/DesktopComputerUse.ProfileBuilder.Copilot -- `
  <profile> <snapshot.json> <semantic-key> [model]
```

It sends only the bounded candidate list produced by deterministic ranking. Copilot can select only an existing candidate ID; it cannot invoke UI actions or write a profile. Building this optional project downloads the separately licensed Copilot CLI runtime through `GitHub.Copilot.SDK`.

The command refuses to contact Copilot unless the selected target explicitly sets `"allowAiAssistance": true`. Candidate payloads omit control values and suppress help text for password or redacted controls.

### UIA2 versus UIA3

Start with `uia3`. Repeat qualification with `uia2` for Windows Forms controls, especially:

- `DataGridView`
- Menus
- Combo boxes
- Date and time controls
- Trees
- Modal dialogs
- Owner-drawn and third-party controls

FlaUI documents that some WinForms controls behave better under UIA2. Backend selection is therefore a profile setting rather than a global compile-time choice.

### Selectors

Prefer selectors in this order:

1. A profile-defined `semanticKey`
2. `automationId`
3. `name` plus `controlType` and a stable `ancestor`
4. `className`
5. A bounded zero-based `index` only when the other fields still match more than one control

Example tool selector:

```json
{
  "semanticKey": "save-record"
}
```

Explicit selector:

```json
{
  "automationId": "SaveButton",
  "controlType": "Button",
  "ancestor": {
    "automationId": "CustomerEditorPanel"
  }
}
```

Ambiguous selectors fail and return compact candidate summaries instead of selecting an arbitrary control.

## Run the MCP server

```powershell
dotnet run --project src/DesktopComputerUse.Server/DesktopComputerUse.Server.csproj
```

For a built server:

```powershell
dotnet src/DesktopComputerUse.Server/bin/Debug/net8.0-windows/DesktopComputerUse.Server.dll
```

Self-contained release archives contain an executable named `desktop-computer-use` on Linux or `desktop-computer-use.exe` on Windows.

Launch a self-contained release **directly**. Do not use `dotnet desktop-computer-use.exe`; the `dotnet` host treats the native app host as a framework-dependent assembly and reports a missing `hostpolicy.dll` or `.runtimeconfig.json`.

Windows MCP configuration:

```json
{
  "servers": {
    "desktop-computer-use": {
      "type": "stdio",
      "command": "C:/Users/you/Downloads/desktop-computer-use-windows-x64/desktop-computer-use.exe",
      "args": [],
      "env": {
        "DESKTOP_COMPUTER_USE_PROFILES": "C:/automation/profiles"
      }
    }
  }
}
```

PowerShell smoke test:

```powershell
& "C:\Users\you\Downloads\desktop-computer-use-windows-x64\desktop-computer-use.exe"
```

Configure the command as a stdio MCP server in the host application. MCP host configuration formats vary, but a typical entry resembles:

```json
{
  "servers": {
    "desktop-computer-use": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "C:/automation/DesktopComputerUse.Server.dll"
      ],
      "env": {
        "DESKTOP_COMPUTER_USE_PROFILES": "C:/automation/profiles"
      }
    }
  }
}
```

The `dotnet` command form above is only for a framework-dependent `.dll` produced by a normal build. Use the executable directly for downloaded release archives.

Do not redirect server logs to stdout. MCP protocol messages use stdout; the server configures console logging to stderr.

## MCP tools

| Tool | Purpose |
|---|---|
| `list_application_profiles` | List configured application profiles |
| `launch_application` | Launch and attach through a selected profile |
| `attach_application` | Verify and attach to an existing process |
| `detach_application` | Release the active automation session |
| `get_application_state` | Return process, profile, window, and backend state |
| `inspect_controls` | Return a bounded, redacted control subtree |
| `snapshot_application_schema` | Return a rich, bounded UIA snapshot with snapshot-local candidate IDs and a view signature |
| `resolve_control_intent` | Run exact and deterministic fuzzy ranking without performing an action |
| `get_profile_update_proposals` | List shadow-mode selector-healing proposals |
| `export_profile_update_patch` | Export a reviewed semantic-target patch without writing files |
| `capture_control_image` | Return a selected control as an MCP image block after redacting sensitive descendants |
| `find_control` | Resolve exactly one control |
| `get_control_properties` | Return control properties and supported patterns |
| `invoke_control` | Use the UI Automation Invoke pattern |
| `set_control_value` | Set a writable UI Automation Value pattern |
| `select_control_item` | Use the SelectionItem pattern |
| `set_expanded_state` | Use the ExpandCollapse pattern |
| `scroll_control` | Use bounded semantic scroll increments |
| `wait_for_state` | Wait for existence, value, name, enabled, or visibility state |
| `capture_application_window` | Capture only the attached main window when profile-enabled |

The server permits one active application session at a time. This avoids concurrent state-changing actions racing on the same interactive desktop.

The server also provides the read-only MCP prompts `profile_application`, `add_semantic_target`, and `review_profile_healing` for VS Code and other MCP clients that support prompts.

## Qualification workflow

Before using a real application:

1. Inspect representative forms with Accessibility Insights for Windows or the Windows SDK `Inspect.exe`.
2. Record stable automation IDs, names, control types, and ancestors.
3. Create an application profile with screenshots disabled.
4. Test launch or verified process attachment.
5. Inspect only a small subtree first.
6. Exercise text, buttons, grids, menus, trees, tabs, and modal dialogs.
7. Repeat the same workflow with UIA2 and UIA3.
8. Test 100%, 125%, and 150% DPI when screenshots or pixel-sensitive custom controls matter.
9. Test local console, RDP reconnect, lock/unlock, process restart, and multiple instances.
10. Add image/OCR automation only for controls with no usable accessibility provider.

## Security boundaries

- Use a dedicated, non-administrator Windows account or disposable VM.
- Keep the MCP server local over stdio.
- Do not run the UI Automation worker as a normal Windows service; services run outside the user's interactive desktop.
- Run the target and server at the same integrity level.
- Do not automate UAC secure-desktop prompts.
- Mark password and sensitive fields with `sensitiveAutomationIds`.
- Enable screenshots only for profiles that require them.
- Control and window captures black out password controls and automation IDs listed in `sensitiveAutomationIds`; direct capture of a sensitive control is rejected.
- Review stderr audit logs without recording field values.
- Keep profile directories writable only by trusted administrators or the dedicated automation account.
- Treat names, help text, labels, OCR, screenshots, and other observed UI content as untrusted data.
- Do not use model-reported confidence as authorization to act.
- Review profile patches outside the action server before applying them.

An attached process must have the same normalized executable path configured by its profile. A caller cannot supply an arbitrary path to launch or attach.

## Project structure

```text
src/
  DesktopComputerUse.Contracts/   Typed profiles, selectors, results, and errors
  DesktopComputerUse.Automation/  Policy, MTA worker, FlaUI backends, and actions
  DesktopComputerUse.ProfileIntelligence/ Vendor-neutral bounded ranking interface
  DesktopComputerUse.ProfileIntelligence.Copilot/ Optional Copilot SDK adapter
  DesktopComputerUse.ProfileBuilder/ Snapshot, ranking, draft, diff, and apply CLI
  DesktopComputerUse.ProfileBuilder.Copilot/ Optional Copilot-backed ranking CLI
  DesktopComputerUse.Server/      MCP stdio host and tools
  DesktopComputerUse.Server.Linux/ Portable MCP companion with explicit platform errors
tests/
  DesktopComputerUse.Automation.Tests/
  DesktopComputerUse.Server.Tests/
  DesktopComputerUse.ProfileIntelligence.Tests/
  DesktopComputerUse.TestApp/     Deterministic Windows Forms fixture
profiles/
  example.application.json
```

## Current limitations

- Native UI Automation execution requires Windows and an interactive desktop.
- The server controls one application session at a time.
- Hard cancellation cannot interrupt every blocking operating-system UIA call; operations are serialized and use cooperative timeouts.
- Process attachment verifies executable paths but does not yet enforce Authenticode publisher identity.
- Owner-drawn controls may require a future bounded image/OCR adapter.
- Fuzzy resolution is diagnostic/shadow-mode and does not silently replace configured selectors for mutations.
- Copilot structured output used by the optional adapter is isolated behind an SDK API currently marked for evaluation.
- The initial implementation does not provide remote HTTP transport.
- The Linux release cannot operate Windows Forms controls; it exists for profile discovery, MCP compatibility checks, and explicit platform diagnostics.

## GitHub releases

The [`release.yml`](.github/workflows/release.yml) workflow runs when a strict semantic-version tag matching `vX.X.X` is pushed, or through manual workflow dispatch with a `vX.X.X` release tag. Other tag formats fail before build or publication.

For example:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The tag is the single version source. For tag `v1.2.3`, the workflow publishes with:

- NuGet/product version and informational version `1.2.3`
- Assembly and file version `1.2.3.0`

The workflow verifies the executable metadata and attaches:

- `desktop-computer-use-windows-x64-v1.2.3.zip`
- `desktop-computer-use-windows-arm64-v1.2.3.zip`
- `desktop-computer-use-linux-x64-v1.2.3.tar.gz`
- `desktop-computer-use-linux-arm64-v1.2.3.tar.gz`
- `desktop-computer-use-profile-builder-windows-x64-v1.2.3.zip`
- `desktop-computer-use-profile-builder-windows-arm64-v1.2.3.zip`
- `SHA256SUMS.txt`

Each platform archive contains:

- `VERSION`, containing `1.2.3`
- `release-manifest.json`, containing the tag, semantic version, assembly version, component, runtime identifier, and Git commit
- The self-contained executable, README, and example profile
- `appsettings.json` in server archives

See [release artifacts and versioning](docs/releases.md) for architecture selection, checksum verification, archive contents, and release creation.

Manual dispatch can create the validated release tag at the selected commit when the tag does not already exist.

## Licenses

The implementation uses permissively licensed open-source components:

- MCP C# SDK: Apache-2.0
- FlaUI: MIT
- .NET: MIT

WinAppDriver is not used.

The optional `GitHub.Copilot.SDK` wrapper is MIT-licensed, but it downloads/uses the separately licensed GitHub Copilot CLI runtime and normally uses proprietary model services. The MCP server and deterministic profile builder core remain usable without Copilot. Review the [Copilot CLI license](https://github.com/github/copilot-cli/blob/main/LICENSE.md) before redistributing a Copilot-enabled profile builder.
