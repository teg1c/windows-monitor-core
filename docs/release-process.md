# Windows Monitor Release Process

Release repository:

```text
git@github.com:teg1c/windows-monitor-release.git
```

This repository is for software release artifacts only. Do not copy source code into it.

## Build Locally

```powershell
.\build.ps1 -Version 0.1.0
```

If the build should contain a remote license validation endpoint:

```powershell
.\build.ps1 -Version 0.1.0 -LicenseValidationUrl "https://example.com/api/license/check"
```

The build output is written to `dist`:

- `dist\WindowsMonitor` runnable app directory
- `dist\WindowsMonitor-v0.1.0-win-x64.zip`
- `dist\WindowsMonitor-v0.1.0-win-x64.zip.sha256`
- `dist\WindowsMonitor-v0.1.0-manifest.json`
- `dist\latest.json`

## Prepare Release Repository

```powershell
.\publish-release.ps1 -Version 0.1.0
```

With license validation config:

```powershell
.\publish-release.ps1 -Version 0.1.0 -LicenseValidationUrl "https://example.com/api/license/check"
```

This clones or updates the release repository under `dist\release-repo`, then copies only release artifacts:

```text
latest.json
releases/v0.1.0/WindowsMonitor-v0.1.0-win-x64.zip
releases/v0.1.0/WindowsMonitor-v0.1.0-win-x64.zip.sha256
releases/v0.1.0/WindowsMonitor-v0.1.0-manifest.json
```

To push the prepared release branch and tag:

```powershell
.\publish-release.ps1 -Version 0.1.0 -Push
```

## GitHub Releases

The application update page checks GitHub Releases from `teg1c/windows-monitor-release`.

If GitHub CLI is installed and authenticated, the release assets can be uploaded by the script:

```powershell
.\publish-release.ps1 -Version 0.1.0 -Push -CreateGitHubRelease
```

For online update detection, create a GitHub Release in that repository using the same tag, such as `v0.1.0`, and upload these assets:

- `WindowsMonitor-v0.1.0-win-x64.zip`
- `WindowsMonitor-v0.1.0-win-x64.zip.sha256`
- `WindowsMonitor-v0.1.0-manifest.json`

The release repository can remain source-free while still providing versioned software packages.
