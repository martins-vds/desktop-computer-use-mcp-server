# Azure Artifact Signing setup

Windows release executables are prepared for Authenticode signing through [Azure Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/).

## Current onboarding state

Completed:

- Registered the `Microsoft.CodeSigning` Azure resource provider.
- Installed the Azure CLI `artifact-signing` extension.
- Created Basic Artifact Signing account `dcusign1396259398` in East US.
- Created Microsoft Entra application/service principal `github-desktop-computer-use-artifact-signing`.
- Created a secretless GitHub OIDC federated credential.
- Assigned the OIDC subject to the protected `artifact-signing` GitHub environment.
- Created GitHub environment branch policy `main`.
- Created GitHub environment tag policy `v*`.
- Added repository owner `martins-vds` as the required environment reviewer.
- Added environment-scoped Azure identity secrets.
- Added environment-scoped account, endpoint, and profile variables.
- Split Linux and Windows release jobs.
- Added signing and signature-verification steps to the Windows release job.

Pending portal-only work:

1. Complete Public Trust identity validation.
2. Create the Public Trust certificate profile.
3. Assign signer RBAC to the service principal at certificate-profile scope.
4. Set the repository variable `AZURE_ARTIFACT_SIGNING_ENABLED` to `true`.
5. Run and verify a staging release.

Signing defaults to disabled when the repository enablement variable is missing
or `false`, so releases can continue before portal validation is complete.
Unsigned Windows builds use the `release-unsigned` environment, not the protected
`artifact-signing` environment, and skip Azure login, signing, and signature
verification. Each Windows package records its actual state in
`release-manifest.json`.

## Azure resources

```text
Subscription: active repository automation subscription
Resource group: rg-desktop-computer-use-signing
Region: East US
Endpoint: https://eus.codesigning.azure.net
Artifact Signing account: dcusign1396259398
SKU: Basic
Planned certificate profile: desktop-computer-use-public
```

The Basic SKU is a billed Azure resource. Review current pricing at:

- [Artifact Signing pricing](https://azure.microsoft.com/en-us/pricing/details/artifact-signing/)

## GitHub OIDC identity

Environment:

```text
artifact-signing
```

Issuer:

```text
https://token.actions.githubusercontent.com
```

Audience:

```text
api://AzureADTokenExchange
```

Immutable subject:

```text
repo:martins-vds@41807230/desktop-computer-use-mcp-server@1396259398:environment:artifact-signing
```

The subject includes immutable GitHub owner and repository IDs because the repository uses immutable OIDC subjects. It must match the Entra federated credential exactly.

## GitHub environment values

Secrets:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
```

Environment-scoped variables:

```text
AZURE_ARTIFACT_SIGNING_ENDPOINT=https://eus.codesigning.azure.net
AZURE_ARTIFACT_SIGNING_ACCOUNT=dcusign1396259398
AZURE_ARTIFACT_SIGNING_PROFILE=desktop-computer-use-public
```

Repository variable (not environment-scoped):

```text
AZURE_ARTIFACT_SIGNING_ENABLED=false
```

The enablement decision happens before the Windows job enters an environment.
An existing environment-scoped `AZURE_ARTIFACT_SIGNING_ENABLED` is no longer used;
set the repository variable to control tag releases.

No Azure client secret is stored.

## Complete Public Trust validation

Identity validation is available only through the Azure portal.

1. Open the Artifact Signing account `dcusign1396259398`.
2. Open **Identity validations**.
3. Create an **Organization** or eligible **Individual** Public Trust identity.
4. Complete email, organization, representative, and document verification.
5. Wait for the validation status to become **Completed**.

Microsoft documents geographic and identity requirements at:

- [Artifact Signing quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)

Use Public Trust for GitHub Release executables. Private Trust and Public Trust Test are not default-trusted production signing identities for public downloads.

## Create the certificate profile

After validation completes, obtain its resource ID and run:

```bash
RESOURCE_GROUP="rg-desktop-computer-use-signing"
SIGNING_ACCOUNT="dcusign1396259398"
CERTIFICATE_PROFILE="desktop-computer-use-public"
IDENTITY_VALIDATION_ID="<completed-public-identity-validation-resource-id>"

az artifact-signing certificate-profile create \
  --resource-group "$RESOURCE_GROUP" \
  --account-name "$SIGNING_ACCOUNT" \
  --name "$CERTIFICATE_PROFILE" \
  --profile-type PublicTrust \
  --identity-validation-id "$IDENTITY_VALIDATION_ID"
```

Confirm:

```bash
az artifact-signing certificate-profile show \
  --resource-group "$RESOURCE_GROUP" \
  --account-name "$SIGNING_ACCOUNT" \
  --name "$CERTIFICATE_PROFILE"
```

## Assign signer RBAC

Retrieve the existing service-principal object ID by display name:

```bash
CLIENT_ID="$(
  az ad app list \
    --display-name github-desktop-computer-use-artifact-signing \
    --query '[0].appId' \
    --output tsv
)"

SERVICE_PRINCIPAL_OBJECT_ID="$(
  az ad sp show \
    --id "$CLIENT_ID" \
    --query id \
    --output tsv
)"

SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
```

Assign only the profile signer role:

```bash
PROFILE_SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/rg-desktop-computer-use-signing/providers/Microsoft.CodeSigning/codeSigningAccounts/dcusign1396259398/certificateProfiles/desktop-computer-use-public"

az role assignment create \
  --assignee-object-id "$SERVICE_PRINCIPAL_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Artifact Signing Certificate Profile Signer" \
  --scope "$PROFILE_SCOPE"
```

Do not grant Owner or Contributor to the workflow identity.

## Enable signing

After the profile and RBAC assignment are active:

```bash
gh variable set AZURE_ARTIFACT_SIGNING_ENABLED \
  --repo martins-vds/desktop-computer-use-mcp-server \
  --body true
```

Confirm:

```bash
gh variable list \
  --repo martins-vds/desktop-computer-use-mcp-server
```

Manual runs also expose an `artifact_signing` choice:

| Mode | Behavior |
| --- | --- |
| `auto` (default) | Use the repository variable; missing or `false` means unsigned |
| `enabled` | Require signing for this run, regardless of the repository variable |
| `disabled` | Publish unsigned Windows artifacts for this run, regardless of the repository variable |

Tag-triggered releases always use `auto`. Signing failures never silently
downgrade an enabled run to unsigned artifacts.

## Workflow behavior

The release workflow:

1. Tests the solution.
2. Publishes Linux artifacts without Azure access.
3. Resolves signing once for all four Windows artifacts: enabled runs use the
   protected `artifact-signing` environment; disabled runs use `release-unsigned`.
4. Validates semantic assembly versions.
5. Logs in to Azure with GitHub OIDC when signing is enabled.
6. When enabled, signs the final self-contained EXE.
7. Uses SHA-256 and Microsoft's RFC 3161 timestamp service for signing.
8. When enabled, verifies the signature with SignTool and `Get-AuthenticodeSignature`.
9. Requires a timestamp countersignature for signed executables.
10. Packages the executable, with a manifest declaring whether it was signed.
11. Generates checksums over final archives.

Pinned Azure actions:

```text
azure/login v3.1.0
azure/artifact-signing-action v2.0.0
```

The workflow references full release commit SHAs.

## Staging verification

Before the first production release:

1. Run the release workflow manually with a new test version and
   `artifact_signing: enabled`.
2. Approve the `artifact-signing` environment if protection rules require it.
3. Confirm all four Windows matrix legs sign successfully.
4. Download and extract each Windows ZIP.
5. Verify:

```powershell
Get-AuthenticodeSignature .\desktop-computer-use.exe | Format-List *
Get-AuthenticodeSignature .\desktop-computer-use-profile-builder.exe | Format-List *
```

6. Run SignTool:

```powershell
signtool verify /pa /all /v /tw .\desktop-computer-use.exe
```

7. Test the Windows ARM64 artifacts on an ARM64 Windows device.
8. Confirm `release-manifest.json` contains:

```json
"authenticodeSigned": true,
"signingProvider": "Azure Artifact Signing"
```

## Linux releases

Linux ELF executables and `.tar.gz` files are not Authenticode-signed. Their manifests explicitly report:

```json
"authenticodeSigned": false,
"signingProvider": "none"
```

Use the release SHA-256 checksums for integrity. A future Linux-native signing strategy can use artifact attestations, Sigstore/cosign, or signed package repositories.

## Audit and operations

Enable the `SignTransactions` diagnostic category on the Artifact Signing account and send it to Azure Storage or Event Hubs:

- [Access signed transactions](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-sign-history)

The workflow generates a unique correlation ID for each signing request. Retain:

- GitHub run ID and URL.
- Release tag and commit.
- Correlation ID.
- SignTool verification output.
- Final archive checksum.
- Azure signing transaction record.

Artifact Signing certificates are valid for 72 hours and renew daily. Timestamping is required so signatures remain valid after certificate expiry.

## Disable signing

To temporarily publish unsigned Windows onboarding builds:

```bash
gh variable set AZURE_ARTIFACT_SIGNING_ENABLED \
  --repo martins-vds/desktop-computer-use-mcp-server \
  --body false
```

Or disable signing for one manual release:

```bash
gh workflow run release.yml \
  --repo martins-vds/desktop-computer-use-mcp-server \
  -f tag=v1.2.3 \
  -f artifact_signing=disabled
```

The `release-unsigned` environment is created automatically by GitHub on first
use if it does not exist. Keep it free of signing secrets and required signing
reviewers so unsigned releases are not blocked by signing approvals.

Packages clearly report the unsigned state. Do not advertise such a release as signed.

## Cleanup

If the signing experiment is abandoned, disable signing before deleting resources:

```bash
gh variable set AZURE_ARTIFACT_SIGNING_ENABLED \
  --repo martins-vds/desktop-computer-use-mcp-server \
  --body false
```

Then remove, in order:

1. Certificate profile.
2. Identity validation/account if no longer needed.
3. Artifact Signing account/resource group.
4. Entra app/service principal.
5. GitHub environment secrets and variables.

Deleting a certificate profile does not revoke previously signed binaries. Certificate revocation is a separate, irreversible process.
