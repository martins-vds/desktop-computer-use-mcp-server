# Desktop Computer Use MCP Server

An open-source Model Context Protocol server for operating allowlisted Windows desktop applications through Microsoft UI Automation.

The native automation implementation uses:

- [.NET 8](https://dotnet.microsoft.com/)
- The official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [FlaUI](https://github.com/FlaUI/FlaUI) with configurable UIA2 and UIA3 backends
- Local stdio transport

Release archives are produced for Windows and Linux on both x64 and ARM64. The Windows executable contains the full FlaUI automation implementation. The Linux executable exposes the same MCP discovery surface and can list profiles, but native Windows Forms actions return an explicit `PlatformNotSupported` result.

The repository still contains the original Playwright browser-test scaffold. Playwright is separate from the native desktop server and is not used to automate normal Windows Forms controls.

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

It intentionally does not expose arbitrary shell commands, PowerShell, executable paths, desktop-wide inspection, coordinate clicking, global typing, or clipboard access.

Image and OCR automation are deferred until qualification against a real application demonstrates that semantic UI Automation is insufficient.

## Requirements

Runtime requirements:

- Windows 10 or Windows 11
- .NET 8 runtime
- An interactive signed-in Windows user session
- The MCP server and target application running as the same user and at compatible integrity levels

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
  -p:IncludeNativeLibrariesForSelfExtract=true
```

```bash
dotnet publish src/DesktopComputerUse.Server.Linux/DesktopComputerUse.Server.Linux.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
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
  "semanticSelectors": {
    "save-record": {
      "automationId": "SaveButton",
      "controlType": "Button"
    }
  },
  "sensitiveAutomationIds": [
    "PasswordTextBox"
  ]
}
```

Relative executable and working-directory paths are resolved from the profile file's directory. The executable is allowed to be absent when profiles are loaded so profiles can be deployed before applications, but launch fails explicitly until the file exists.

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
- Review stderr audit logs without recording field values.
- Keep profile directories writable only by trusted administrators or the dedicated automation account.

An attached process must have the same normalized executable path configured by its profile. A caller cannot supply an arbitrary path to launch or attach.

## Project structure

```text
src/
  DesktopComputerUse.Contracts/   Typed profiles, selectors, results, and errors
  DesktopComputerUse.Automation/  Policy, MTA worker, FlaUI backends, and actions
  DesktopComputerUse.Server/      MCP stdio host and tools
  DesktopComputerUse.Server.Linux/ Portable MCP companion with explicit platform errors
tests/
  DesktopComputerUse.Automation.Tests/
  DesktopComputerUse.Server.Tests/
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
- The initial implementation does not provide remote HTTP transport.
- The Linux release cannot operate Windows Forms controls; it exists for profile discovery, MCP compatibility checks, and explicit platform diagnostics.

## GitHub releases

The [`release.yml`](.github/workflows/release.yml) workflow runs when a tag matching `v*` is pushed, or through manual workflow dispatch with a release tag.

For example:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow builds and attaches:

- `desktop-computer-use-windows-x64.zip`
- `desktop-computer-use-windows-arm64.zip`
- `desktop-computer-use-linux-x64.tar.gz`
- `desktop-computer-use-linux-arm64.tar.gz`
- `SHA256SUMS.txt`

Manual dispatch can create a release tag at the selected commit when the tag does not already exist.

## Licenses

The implementation uses permissively licensed open-source components:

- MCP C# SDK: Apache-2.0
- FlaUI: MIT
- .NET: MIT

WinAppDriver is not used.
