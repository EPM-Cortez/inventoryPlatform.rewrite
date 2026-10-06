# Endpoint-agent MSI deployment

## Package and signing

Build from the `rewrite` directory:

```powershell
.\scripts\build-agent-package.ps1 -Version '1.0.1'
```

This creates a self-contained `win-x64` service and MSI under
`artifacts\agent`. The unsigned package is for development validation only.
Verify the package tables before testing installation:

```powershell
.\scripts\verify-agent-msi.ps1 -ExpectedVersion '1.0.1'
```

The verifier checks per-machine scope, the least-privileged service identity,
automatic start/recovery, secure public properties, repair/upgrade property
restoration, registry configuration, event-log source, legacy-product collision
detection, new-install launch conditions and exact ProgramData SDDL.
Production packaging must provide an Authenticode certificate and make signing
mandatory:

```powershell
.\scripts\build-agent-package.ps1 `
  -Version '1.0.1' `
  -CertificateThumbprint '<code-signing-certificate-thumbprint>' `
  -RequireSignature
```

The script signs the agent before it is embedded, signs the MSI, verifies both
Authenticode signatures, and writes SHA-256 artifact metadata. It also signs the exact
manifest bytes with the certificate's RSA private key and emits
`agent-artifacts.json.sig` plus a public `agent-release-certificate.pem`. Keep the private signing key in the
organisation's approved certificate/HSM workflow, not in the repository or CI
variables as an exported PFX.

The preferred GPO/SCCM workflow publishes the signed MSI and manifest through the
organisation's controlled software-distribution share. If an administrator needs a
browser/API download, configure absolute `AgentInstaller:InstallerPath` and
`AgentInstaller:ManifestPath` values. Then:

```text
GET /api/v1/agents/installer
GET /api/v1/agents/installer/download
```

Both endpoints require `agents.manage` and return `Cache-Control: no-store`. The API
reopens and hashes the MSI for every request and streams the validated file.
On Windows, Production verifies MSI Authenticode trust. On Linux, Production verifies
the detached manifest signature with a separately provisioned trusted certificate,
then requires its exact approved version and MSI SHA-256 before streaming the hashed
MSI. This is a release attestation; the Linux host does not directly perform Windows
Authenticode verification. Both hosts refuse the current unsigned development
artifact. The returned command template uses
an `ENROLLMENT_TOKEN=<one-time-enrollment-token>` placeholder. The API never
substitutes or returns a real enrolment token in installer metadata.
On Linux the metadata reports `releaseAttestationVerified: true` and `signed: false`;
the latter means embedded MSI Authenticode was not checked by the host. On Windows,
`signed: true` indicates successful Authenticode verification.

For the Linux Docker deployment, use `compose.agent-installer.yml` with
`compose.postgres.yml` only after reviewing a signed release. Set
`INVENTORY_AGENT_ARTIFACT_DIR` to the absolute artifact directory, copy the release
certificate through an **independent trusted channel** outside that directory and
set `INVENTORY_AGENT_TRUSTED_CERT_FILE` to its absolute path. Set
`INVENTORY_AGENT_APPROVED_VERSION` and `INVENTORY_AGENT_APPROVED_SHA256` from the
release approval record, not from the artifact directory at container startup. The
overlay mounts artifacts read-only and the trusted certificate as a separate Docker
secret. Rotate the pinned certificate and approved digest deliberately for each
release. A missing or invalid release attestation, or an MSI that differs from
the approved digest, fails closed. Embedded Authenticode validity still needs
checking in the Windows release/pilot process.

## Installer properties

| Public property | Required | Meaning |
| --- | --- | --- |
| `API_BASE_URL` | New install | HTTPS origin of the Inventory API, with no path/query/credentials |
| `ENROLLMENT_TOKEN` | Optional bootstrap mode | Token returned by the enrollment-profile create response; mutually exclusive with `ENROLLMENT_TOKEN_FILE` |
| `ENROLLMENT_TOKEN_FILE` | For enrollment | Absolute local token-file path; this is a path, not the token |
| `ALLOW_SOFTWARE_KEY` | No | `0` by default; set `1` only for an approved non-TPM exception |

`ENROLLMENT_TOKEN` is a convenience for an individual installation or a tightly
controlled pilot. The property is hidden from Windows Installer logging and the
installer immediately writes it to a machine-DPAPI-protected bootstrap file. The agent
deletes that file after enrollment succeeds, and MSI rollback or a normal uninstall
also removes an unconsumed protected token. The original `msiexec` command can still be
visible in elevated process inspection and shell history, so use a short expiry and the
smallest practical `maxEnrollments`; use `maxEnrollments: 1` for a genuinely single-use
token.

The agent submits the token only while enrolling. Successful enrollment gives the
device its own signing identity; ordinary check-ins do not reuse the enrollment token.
If a profile has `maxEnrollments` greater than one, the same server token can enroll up
to that many devices before it expires or is revoked.

On repair and major upgrade, omit these public properties to retain the existing
`ServerBaseUrl`, `EnrollmentTokenFilePath`, and `AllowSoftwareKey` registry values.
Supplying a property explicitly is an intentional configuration change. This matters
for software-key devices: an omitted `ALLOW_SOFTWARE_KEY` no longer silently resets
their policy to `0`.

Example silent installation for one device:

```powershell
msiexec.exe /i .\Inventory.Agent.Installer.msi `
  /qn /norestart `
  API_BASE_URL='https://inventory.school.example' `
  ENROLLMENT_TOKEN='<one-time-enrollment-token>' `
  ALLOW_SOFTWARE_KEY=0 `
  /L*v 'C:\Windows\Temp\Inventory-Agent-Install.log'
```

Normal Windows Installer return codes `0` and `3010` are successful. The service
starts automatically and safely waits when neither bootstrap source has been staged.
For a same-version repair, add `REINSTALL=ALL REINSTALLMODE=vomus`.

## Create and close a rollout profile

Create a narrowly scoped profile through
`POST /api/v1/agents/enrollment-profiles`:

```json
{
  "name": "ICT Windows pilot",
  "expiresAtUtc": "2026-07-27T17:00:00Z",
  "maxEnrollments": 25,
  "requireTpm": false,
  "allowedHostnameSuffix": "school.example"
}
```

The clear token is returned once. Treat it as a short-lived deployment secret. Prefer
a separate profile per pilot/OU/collection, use the smallest practical device cap and
expiry, monitor `enrolledCount`, and revoke it as soon as the deployment finishes.
`requireTpm: true` currently fails closed with `agent_tpm_attestation_unavailable`:
the agent reports TPM key protection, but the server has no trusted attestation
issuer and cannot verify that report. This also applies to key-recovery approval.
Do not describe existing `keyProtection: tpm` agents as hardware-verified. Agent
responses expose `hardwareAttestationVerified: false` until a cryptographic
attestation protocol and trusted issuer are deployed and existing devices are
re-enrolled or explicitly re-attested. A software-key-compatible profile can be
used for a controlled pilot, but it does not provide hardware trust.

For a shared-token mass rollout, continue to use `ENROLLMENT_TOKEN_FILE` and stage the
token only on an access-controlled
deployment share or through the SCCM secret/content workflow. Never embed it in an
MST, public SYSVOL script, source control or ordinary GPO preference.

After the MSI has created the virtual service identity, secure a local staged token:

```powershell
$tokenPath = 'C:\ProgramData\Inventory\Enrollment\enrollment.token'
$tokenDirectory = Split-Path -Parent $tokenPath
New-Item -ItemType Directory -Path $tokenDirectory -Force | Out-Null
Copy-Item -LiteralPath '<restricted-source-token-file>' -Destination $tokenPath

icacls.exe $tokenDirectory /inheritance:r `
  /grant:r 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F'
icacls.exe $tokenPath /inheritance:r `
  /grant:r 'SYSTEM:F' 'Administrators:F' `
  '*S-1-5-80-2609206542-791502006-4278732640-3842405600-3050437999:M'
```

The service deletes the local file after protected enrollment state has been saved.
Remove the source copy and revoke the enrollment profile after the rollout.

The server-advertised check-in interval is persisted in protected local state and is
the cadence source of truth after enrollment. A stable per-device startup spread and
bounded exponential retry prevent a GPO/SCCM ring sharing one proxy from checking in
simultaneously. The offline queue remains strict FIFO: a backed-off older sequence
blocks newer snapshots until it succeeds or receives a terminal rejection.

## Group Policy Software Installation

GPO computer assignment accepts an MSI from a UNC distribution point but does not
provide a safe place for a secret command line:

1. Put the signed MSI on a read-only UNC share accessible to the target computer
   accounts.
2. Create an MST with Orca or the organisation's packaging tool. Set only the
   non-secret `API_BASE_URL`, `ENROLLMENT_TOKEN_FILE`, and
   `ALLOW_SOFTWARE_KEY=0` properties.
3. In **Computer Configuration > Policies > Software Settings > Software
   installation**, add the UNC MSI as **Assigned** and attach the MST on the
   Modifications tab before completing the package.
4. Use a computer startup script or management control with access to a restricted
   token source to stage and ACL the local token after software installation.
5. Reboot/policy-refresh a pilot OU, confirm enrollment, then expand in controlled
   rings.

Do not put the token text in the MST or a broadly readable SYSVOL script. GPO
assignment installs in the computer context before logon, which is appropriate for
the per-machine service.

## SCCM / Configuration Manager

Create an **Application** with a Windows Installer deployment type:

- install for system whether or not a user is logged on;
- use the silent `msiexec` command above;
- distribute the signed MSI and separately protected token content;
- use the MSI product-code detection generated for that version, or the
  `HKLM\SOFTWARE\Inventory\Agent\InstalledVersion` registry value;
- configure `0` and `3010` as success; and
- use application supersedence for later major-upgrade MSIs.

Stage the token in a preceding dependency/task step under SYSTEM, apply the exact file
ACL, then install the MSI. A task-sequence variable must contain only the token-file
path unless the organisation's SCCM secret handling explicitly protects the token
value. Delete cached token content and revoke the server profile after deployment.

## Verification

On a pilot device:

```powershell
Get-Service -Name InventoryEndpointAgent
Get-ItemProperty -Path 'HKLM:\SOFTWARE\Inventory\Agent'
Get-AuthenticodeSignature `
  'C:\Program Files\Inventory\Agent\Inventory.Agent.exe'
Get-WinEvent -FilterHashtable @{
  LogName = 'Application'
  ProviderName = 'Inventory Endpoint Agent'
} -MaxEvents 20
```

Then verify:

- the service is running under `NT SERVICE\InventoryEndpointAgent`;
- the token file has been deleted;
- `C:\ProgramData\Inventory\Agent\agent-state.bin` is not readable by ordinary users;
- the pre-registered Application event-log source receives startup, retry and
  rejection events under the least-privileged service account;
- the API shows the key as TPM protected and a recent check-in;
- the snapshot is linked only to the expected asset; and
- audit contains profile creation, enrollment, any link and eventual profile
  revocation.

## Upgrade and removal

The MSI uses a stable UpgradeCode and major upgrades. Preserve the agent's ProgramData
state and CNG key during an upgrade so its server identity and sequence continue.
Repair and upgrade without new configuration values are supported:

```powershell
msiexec.exe /fa '{current-product-code}' /qn /norestart
msiexec.exe /i .\Inventory.Agent.Installer.msi /qn /norestart
```

Use the second command with explicit public properties only when changing those
settings. Validate repair and upgrade in a Windows 10/11 pilot before broad
supersedence.

The former **Inventory TPM Agent** cannot coexist with this rewrite: both products
use `C:\Program Files\Inventory\Agent`. The rewrite MSI detects the old UpgradeCode,
the `InventoryTpmAgent` service, and its legacy registry marker, and blocks a new
installation or maintenance repair rather than overwriting a running legacy binary.
A full rewrite-agent uninstall remains available so the collision can be recovered
safely. Remove the old product through the same GPO/SCCM deployment that installed
it, then verify:

```powershell
Get-Service -Name InventoryTpmAgent -ErrorAction SilentlyContinue
Get-ItemProperty `
  -Path 'HKLM:\SOFTWARE\Inventory\Agent' `
  -Name Installed `
  -ErrorAction SilentlyContinue
```

Both checks must return no legacy service/marker before installing the rewrite MSI.
Do not manually delete the shared executable directory while either MSI is
registered. Preserve any evidence needed from the legacy deployment first; its
bootstrap identity is not compatible with the rewrite's signed enrollment identity.

Before permanent removal, revoke the managed agent in the API. Uninstall the MSI
through the same management system. Protected state is intentionally retained when
non-empty to avoid silent identity loss during repair/upgrade; remove the exact
`C:\ProgramData\Inventory\Agent` directory and CNG key only through an approved
decommission procedure after revocation and evidence retention.
