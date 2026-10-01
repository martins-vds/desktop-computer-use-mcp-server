# Profile builder guide

The profile builder creates and validates allowlisted application profiles for the desktop computer-use MCP server. It uses FlaUI, so live application discovery is available only on Windows in an interactive user session.

## Download

Download the builder archive matching the Windows architecture from the latest GitHub release:

| Windows architecture | Artifact |
|---|---|
| x64 | `desktop-computer-use-profile-builder-windows-x64-vX.X.X.zip` |
| ARM64 | `desktop-computer-use-profile-builder-windows-arm64-vX.X.X.zip` |

Check the architecture in PowerShell:

```powershell
$env:PROCESSOR_ARCHITECTURE
```

Use the x64 artifact for `AMD64` and the ARM64 artifact for `ARM64`.

The archive is self-contained. A separate .NET installation is not required.

## Verify and extract

Download `SHA256SUMS.txt` from the same release and calculate the archive hash:

```powershell
Get-FileHash .\desktop-computer-use-profile-builder-windows-x64-v1.2.3.zip -Algorithm SHA256
```

Compare the result with the matching line in `SHA256SUMS.txt`, then extract:

```powershell
Expand-Archive `
  .\desktop-computer-use-profile-builder-windows-x64-v1.2.3.zip `
  -DestinationPath .\desktop-profile-builder

Unblock-File .\desktop-profile-builder\desktop-computer-use-profile-builder.exe
```

Check the packaged version:

```powershell
Get-Content .\desktop-profile-builder\VERSION
Get-Content .\desktop-profile-builder\release-manifest.json
```

## Commands

```text
validate <profile>
snapshot <profile> <output.json> [--attach <pid>]
search <profile> <snapshot.json> <semantic-key>
add-target <profile> <snapshot.json> <semantic-key> <intent> <candidate-id> <output-profile> [--pattern <pattern>]
diff <original-profile> <draft-profile>
apply <draft-profile> <target-profile>
```

Show help:

```powershell
.\desktop-computer-use-profile-builder.exe help
```

## Bootstrap a profile

The builder needs an allowlisted executable and main-window rule before it can launch or attach. Start with a minimal file such as `my-app.application.json`:

```json
{
  "schemaVersion": 2,
  "id": "my-app",
  "displayName": "My Windows application",
  "executablePath": "C:/Program Files/My App/MyApp.exe",
  "backend": "uia3",
  "mainWindow": {
    "titleRegex": "^My App"
  },
  "operationTimeoutMs": 10000,
  "pollIntervalMs": 100,
  "maxTreeDepth": 8,
  "maxResults": 500,
  "enableScreenshots": false,
  "semanticTargets": {},
  "sensitiveAutomationIds": []
}
```

Use forward slashes or escaped backslashes in JSON paths.

Validate it:

```powershell
.\desktop-computer-use-profile-builder.exe validate `
  .\my-app.application.json
```

## Capture an application snapshot

Launch the configured executable and capture the active UI Automation tree:

```powershell
.\desktop-computer-use-profile-builder.exe snapshot `
  .\my-app.application.json `
  .\my-app.snapshot.json
```

To attach to an application that is already running:

```powershell
.\desktop-computer-use-profile-builder.exe snapshot `
  .\my-app.application.json `
  .\my-app.snapshot.json `
  --attach 12345
```

Attachment succeeds only when the process executable matches the profile path.

Snapshots include:

- Snapshot-local candidate IDs
- Names and automation IDs
- Control types and classes
- Supported UIA patterns
- Parent, child, sibling, and tree-path relationships
- Nearby labels
- Relative geometry
- A view signature
- Redacted password and sensitive values

Candidate IDs are valid only for the snapshot that produced them. Never store a candidate ID in a profile.

## Add one semantic target

Profile authoring is intentionally incremental. Select and review one target at a time.

Suppose snapshot inspection identifies `node-0017` as the customer-name edit field:

```powershell
.\desktop-computer-use-profile-builder.exe add-target `
  .\my-app.application.json `
  .\my-app.snapshot.json `
  customer-name `
  "Editable customer name field" `
  node-0017 `
  .\my-app.draft.application.json `
  --pattern Value
```

The builder creates:

- A selector strategy
- Expected control type
- Required UIA patterns
- Current view scope
- A persistent control fingerprint

For multiple required patterns, use a comma-separated value:

```powershell
--pattern Invoke,ExpandCollapse
```

## Search an existing target

After a target exists in the profile, rank controls in a saved snapshot:

```powershell
.\desktop-computer-use-profile-builder.exe search `
  .\my-app.draft.application.json `
  .\my-app.snapshot.json `
  customer-name
```

The output contains:

- Resolution status
- Best candidate and score
- Margin from the runner-up
- Ranked candidates
- Feature-level evidence

An ambiguous result is intentional. Refine strategies, view scope, expected type, required patterns, or ancestor evidence rather than selecting an arbitrary control.

## Review and apply

Show semantic differences:

```powershell
.\desktop-computer-use-profile-builder.exe diff `
  .\my-app.application.json `
  .\my-app.draft.application.json
```

Apply only after review:

```powershell
Copy-Item .\my-app.application.json .\my-app.application.json.backup

.\desktop-computer-use-profile-builder.exe apply `
  .\my-app.draft.application.json `
  .\my-app.application.json
```

The MCP action server never writes profile files. Profile changes require this explicit management step.

## UIA2 and UIA3

Start with `uia3`. Repeat representative snapshots with `uia2` when WinForms grids, menus, combo boxes, date controls, trees, or third-party controls are missing or behave incorrectly.

Change:

```json
"backend": "uia2"
```

Capture snapshots from both backends and compare the evidence before choosing one.

## Sensitive controls

List sensitive automation IDs:

```json
"sensitiveAutomationIds": [
  "PasswordTextBox",
  "AccountNumberTextBox"
]
```

The snapshot redacts their values. Image capture also blacks out password and configured sensitive descendants, and direct capture of a sensitive control is rejected.

## Optional Copilot ranking

The release profile-builder archive contains the deterministic builder. The optional Copilot-backed ranking CLI is currently a source-build feature:

```powershell
dotnet run --project src/DesktopComputerUse.ProfileBuilder.Copilot -- `
  .\my-app.draft.application.json `
  .\my-app.snapshot.json `
  customer-name `
  auto
```

The target must explicitly set:

```json
"allowAiAssistance": true
```

Copilot receives only the bounded, sanitized candidate list. It cannot invoke the application or write the profile.

## Troubleshooting

### Main window is not found

- Confirm the executable path.
- Inspect the exact title and class with Accessibility Insights or `Inspect.exe`.
- Prefer a stable title token over a full dynamic title.
- Increase `operationTimeoutMs` for slow startup.

### Snapshot is incomplete

- Increase `maxTreeDepth` or `maxResults`.
- Navigate to the relevant tab or dialog before using `--attach`.
- Check whether the control is virtualized and appears only after scrolling or expansion.
- Compare UIA2 and UIA3.

### Access is denied

- Run the builder as the same Windows user as the target.
- Use matching integrity levels.
- Avoid automating elevated applications where possible.
- UAC secure-desktop prompts are unsupported.

### Candidate is ambiguous

- Add the expected control type.
- Add required UIA patterns.
- Add an exact automation-ID strategy when stable.
- Scope the target to the correct view or ancestor.
- Capture a snapshot in the exact workflow state where the target is used.
