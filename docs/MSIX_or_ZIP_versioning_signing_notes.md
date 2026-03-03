# RELEASE.md — Release Packaging Plan & Build Artifacts

This document describes how to produce **release-ready Windows packages** for the Genesys Extension Audit desktop app, including **artifact types (MSIX / ZIP)**, **versioning**, **signing**, and **CI-friendly build commands**.

---

## 1. Release goals

A release should provide:

- A **Windows desktop installer** suitable for most users (recommended: **MSIX**).
- A **portable build** for environments where MSIX is not allowed (fallback: **ZIP**).
- Clear **versioning** that matches Git tags and embedded app version metadata.
- Optional (but recommended) **code signing** guidance for enterprise deployment and SmartScreen reputation.

---

## 2. Artifact types

### 2.1 MSIX (recommended)

**Best for**: enterprise distribution, managed installs, clean uninstall, automatic updates (if you later add an App Installer feed).

**Pros**

- Supports signing and integrity validation
- Cleaner install/uninstall
- Supports dependency installation and identity

**Cons**

- Requires a signing certificate for best UX
- Some locked-down environments restrict MSIX

**Output example**

- `GenesysExtensionAudit_<version>_x64.msix`

> If your solution does not currently include a Windows Application Packaging Project (.wapproj), add one to generate MSIX cleanly. If you already have one, use it as the MSIX packaging source of truth.

---

### 2.2 ZIP (portable fallback)

**Best for**: quick internal distribution, restricted environments, “no-install” usage.

**Pros**

- Simple download/unzip/run
- No installer restrictions

**Cons**

- No install identity, no clean uninstall
- SmartScreen warnings likely without signing
- Users can run outdated copies

**Output example**

- `GenesysExtensionAudit_<version>_win-x64.zip`

ZIP should contain a self-contained published output (so users do **not** need the .NET runtime installed).

---

## 3. Supported platforms / runtimes

Recommended minimum release target:

- `win-x64`

Optional additional targets if required:

- `win-arm64`

> Keep the runtime identifier (RID) in the filename to avoid confusion.

---

## 4. Versioning strategy

### 4.1 Semantic versioning (SemVer)

Use `MAJOR.MINOR.PATCH` (e.g., `1.4.2`).

- **MAJOR**: breaking changes, major UI/behavior changes
- **MINOR**: new features, new exports, new config options
- **PATCH**: bug fixes, performance fixes, QA fixes (pagination, 429 handling, cancellation)

### 4.2 Source control tags

Release tags should be:

- `v1.4.2`

### 4.3 Where version must appear

At a minimum, the same version should be applied to:

- Assembly/File version
- Application display version shown in UI (if implemented)
- Package version (MSIX)

Recommended mapping:

- `Version` / `AssemblyVersion`: `1.4.2.0` (or keep AssemblyVersion stable at major only if you prefer)
- `FileVersion`: `1.4.2.0`
- `InformationalVersion`: `1.4.2+<gitsha>` (CI can set this)
- `MSIX Package Version`: must be `Major.Minor.Build.Revision` (4-part numeric)

**MSIX version note:** MSIX requires `A.B.C.D` numeric. Suggested conversion:

- `SemVer 1.4.2` → `1.4.2.0`
- For CI builds: `1.4.2.<runNumber>` if you need unique packages (but keep official releases as `.0`)

---

## 5. Build prerequisites (release machine / CI runner)

- Windows build agent (required for WPF/MSIX workflows)
- .NET SDK 8.x
- If producing MSIX via Packaging Project:
  - Visual Studio Build Tools (or VS) with Windows SDK & MSIX packaging components
- Signing tools (optional but recommended):
  - `signtool.exe` (Windows SDK)
  - Access to a code signing cert (PFX) or certificate from a secure signing service

---

## 6. Build outputs and directory conventions

Recommended convention from repository root:

- `artifacts/`
  - `msix/`
  - `zip/`
  - `checksums/`
  - `sbom/` (optional)
  - `logs/` (optional)

Each release should produce:

1. MSIX package (optional if not supported yet)
2. Portable ZIP containing self-contained publish output
3. SHA-256 checksums for all distributed files

---

## 7. Build commands (ZIP / self-contained publish)

### 7.1 Publish self-contained (win-x64)

From repo root:

```powershell
$Version = "1.0.0"

dotnet restore

dotnet publish .\src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:DebugType=none `
  /p:DebugSymbols=false `
  /p:Version=$Version `
  /p:FileVersion="$Version.0" `
  /p:AssemblyVersion="$Version.0" `
  /p:InformationalVersion="$Version"
```

Output location (typical):

- `src/GenesysExtensionAudit.App/bin/Release/net8.0-windows/win-x64/publish/`

### 7.2 Create ZIP

```powershell
$Version = "1.0.0"
$Rid = "win-x64"
$PublishDir = ".\src\GenesysExtensionAudit.App\bin\Release\net8.0-windows\$Rid\publish"
$OutDir = ".\artifacts\zip"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$ZipPath = Join-Path $OutDir "GenesysExtensionAudit_$Version_$Rid.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath
```

**ZIP contents should include**

- `GenesysExtensionAudit.App.exe` (or your final app exe name)
- `.dll` dependencies (if not single-file)
- `appsettings.json` (non-secret defaults)
- `README.md` (optional copy for convenience)
- `LICENSE` / notices as applicable

**Do not include**

- Secrets (Client Secret, tokens)
- Developer-only files
- Test data

---

## 8. MSIX packaging (recommended path)

There are two common approaches:

### Option A — Windows Application Packaging Project (.wapproj) (recommended)

1. Add a Packaging Project to the solution (if not already present).
2. Reference the WPF application project.
3. Configure:
   - `TargetPlatformVersion` (Windows SDK)
   - `Package.appxmanifest` identity and display name
   - Version (must be 4-part numeric)
4. Build MSIX from the packaging project:

```powershell
msbuild .\src\GenesysExtensionAudit.Package\GenesysExtensionAudit.Package.wapproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:AppxBundle=Never `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:PackageCertificateKeyFile="path\to\cert.pfx" `
  /p:PackageCertificatePassword="***" `
  /p:AppxPackageSigningEnabled=true
```

> Exact properties vary by your packaging setup; treat the above as a template.

### Option B — `dotnet publish` + MSIX tooling
Possible but less standard for WPF unless you already wired tooling. Prefer Option A unless your repo already uses a different MSIX pipeline.

---

## 9. Signing guidance

### 9.1 Why sign?

- Reduces SmartScreen warnings
- Builds trust in enterprise environments
- Verifies integrity and publisher identity

### 9.2 What to sign

- **MSIX** must be signed to install (self-signed works internally; public releases should use a trusted cert).
- For **ZIP portable distribution**, you may optionally sign:
  - the main `.exe` (Authenticode)
  - and/or provide checksums

### 9.3 Certificate options

- **Enterprise internal distribution**: internal CA certificate is acceptable (users must trust the root).
- **Public distribution**: acquire a public code signing cert (OV or EV).
  - EV improves SmartScreen reputation faster but is more complex/costly.

### 9.4 Signing EXE/DLL (Authenticode)
Example (run on Windows with Windows SDK installed):

```powershell
$FileToSign = ".\src\GenesysExtensionAudit.App\bin\Release\net8.0-windows\win-x64\publish\GenesysExtensionAudit.App.exe"
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a $FileToSign
```

### 9.5 Signing MSIX
MSIX signing is typically done as part of the packaging build (wapproj). Ensure:

- `AppxPackageSigningEnabled=true`
- certificate is present in CI secrets or pulled from a secure store

---

## 10. Checksums (required for releases)

Generate SHA-256 for each distributed file:

```powershell
New-Item -ItemType Directory -Force -Path .\artifacts\checksums | Out-Null

Get-FileHash .\artifacts\zip\*.zip -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
  Out-File -Encoding ascii .\artifacts\checksums\SHA256SUMS.txt

# If MSIX exists:
if (Test-Path .\artifacts\msix\*.msix) {
  Get-FileHash .\artifacts\msix\*.msix -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
    Add-Content -Encoding ascii .\artifacts\checksums\SHA256SUMS.txt
}
```

Ship `SHA256SUMS.txt` alongside the artifacts.

---

## 11. Release naming conventions

Use consistent names so users can tell what they’re downloading:

- `GenesysExtensionAudit_<version>_win-x64.zip`
- `GenesysExtensionAudit_<version>_x64.msix`
- `SHA256SUMS.txt`

If you publish pre-releases:

- append label in GitHub Release title and notes (artifact filenames can remain clean, or include `-rc1` if you prefer).

---

## 12. Release checklist

### 12.1 Pre-release checks (quality gates)

- App runs end-to-end against large tenants (pagination across many pages)
- Rate-limit behavior (429 + Retry-After) validated
- Cancellation tested during:
  - in-flight requests
  - Retry-After delay
- Export correctness:
  - stable headers
  - UTF-8 BOM if enabled
  - proper CSV quoting (commas, quotes, newlines)
- UI remains responsive under load

(These match the QA pass scope described in `qa_specialist_output.md`.)

### 12.2 Packaging checks

- ZIP contains no secrets
- Self-contained output launches on a clean machine
- MSIX installs/uninstalls cleanly (if produced)
- Signatures validate (if signing enabled)
- Checksums generated and included

### 12.3 Documentation checks

- `README.md` updated (scopes, region config, IncludeInactive behavior)
- Include a brief note in the GitHub Release notes about:
  - endpoints audited
  - major changes/fixes
  - known limitations

---

## 13. Minimal CI pipeline outline (suggested)

1. Trigger on tag `v*`
2. Set `$Version` from tag
3. Build + test
4. `dotnet publish` self-contained for `win-x64`
5. ZIP
6. (Optional) Build MSIX packaging project
7. (Optional) Sign outputs
8. Generate checksums
9. Upload artifacts to release

---

## 14. Notes / constraints specific to this app

- The app audits these endpoints:
  - Users: `/api/v2/users?pageSize={PageSize}&pageNumber={n}` with conditional `&state=active`
  - Edge Extensions: `/api/v2/telephony/providers/edges/extensions?pageSize={PageSize}&pageNumber={n}`
- Ensure release notes mention that `IncludeInactive=false` filters users by `state=active`.
- Do not ship any OAuth client secrets in packaged config. Use:
  - environment variables, or
  - user-secrets (dev only), or
  - enterprise secret deployment mechanisms.

---
```


- LOG: Reviewed provided project context (QA plan + README excerpt) to align release packaging goals with app behavior and distribution needs.
- LOG: Drafted RELEASE.md covering artifact types (MSIX + ZIP), versioning strategy (SemVer + MSIX 4-part), signing guidance (MSIX + Authenticode), checksum generation, naming conventions, and a release checklist tied to QA scope.
- LOG: Included Windows/.NET 8 build and publish commands suitable for CI and repeatable local release builds.
