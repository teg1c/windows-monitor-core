param(
    [string]$Version = "",

    [string]$Runtime = "win-x64",

    [string]$LicenseValidationUrl = "",

    [string]$LicenseCryptoKeyBase64 = "",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ReleaseRepository = "git@github.com:teg1c/windows-monitor-release.git",

    [string]$GitHubRepositoryFullName = "teg1c/windows-monitor-release",

    [string]$Branch = "main",

    [switch]$SelfContained,

    [switch]$SkipTests,

    [switch]$Push,

    [switch]$CreateGitHubRelease
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$distRoot = Join-Path $root "dist"
$releaseWorktree = Join-Path $distRoot "release-repo"

$buildParams = @{
    Configuration = $Configuration
    Runtime = $Runtime
}
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $buildParams.Version = $Version
}
if (-not [string]::IsNullOrWhiteSpace($LicenseValidationUrl)) {
    $buildParams.LicenseValidationUrl = $LicenseValidationUrl
}
if (-not [string]::IsNullOrWhiteSpace($LicenseCryptoKeyBase64)) {
    $buildParams.LicenseCryptoKeyBase64 = $LicenseCryptoKeyBase64
}
if ($SelfContained) {
    $buildParams.SelfContained = $true
}
if ($SkipTests) {
    $buildParams.SkipTests = $true
}

& (Join-Path $root "build.ps1") @buildParams
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$manifest = Get-ChildItem -Path $distRoot -Filter "WindowsMonitor-v*-manifest.json" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $manifest) {
    throw "Release manifest not found in $distRoot."
}

$release = Get-Content -Path $manifest.FullName -Raw | ConvertFrom-Json
$zipPath = Join-Path $distRoot $release.package
$shaPath = "$zipPath.sha256"
$latestPath = Join-Path $distRoot "latest.json"

if (-not (Test-Path $zipPath) -or -not (Test-Path $shaPath) -or -not (Test-Path $latestPath)) {
    throw "Release package, checksum, or latest.json is missing."
}

if (Test-Path (Join-Path $releaseWorktree ".git")) {
    git -C $releaseWorktree fetch origin
    git -C $releaseWorktree show-ref --verify --quiet "refs/remotes/origin/$Branch"
    if ($LASTEXITCODE -ne 0) {
        git -C $releaseWorktree checkout -B $Branch
    }
    else {
        git -C $releaseWorktree checkout -B $Branch "origin/$Branch"
        git -C $releaseWorktree pull --ff-only origin $Branch
    }

}
else {
    if (Test-Path $releaseWorktree) {
        Remove-Item -LiteralPath $releaseWorktree -Recurse -Force
    }

    git clone $ReleaseRepository $releaseWorktree
    git -C $releaseWorktree show-ref --verify --quiet "refs/remotes/origin/$Branch"
    if ($LASTEXITCODE -ne 0) {
        git -C $releaseWorktree checkout -B $Branch
    }
    else {
        git -C $releaseWorktree checkout -B $Branch "origin/$Branch"
    }
}

$releaseDir = Join-Path $releaseWorktree "releases\$($release.tag)"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Copy-Item -LiteralPath $zipPath -Destination (Join-Path $releaseDir (Split-Path $zipPath -Leaf)) -Force
Copy-Item -LiteralPath $shaPath -Destination (Join-Path $releaseDir (Split-Path $shaPath -Leaf)) -Force
Copy-Item -LiteralPath $manifest.FullName -Destination (Join-Path $releaseDir (Split-Path $manifest.FullName -Leaf)) -Force
Copy-Item -LiteralPath $latestPath -Destination (Join-Path $releaseWorktree "latest.json") -Force

git -C $releaseWorktree add "latest.json" "releases/$($release.tag)"
git -C $releaseWorktree commit -m "Release $($release.tag)" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No release repository changes to commit." -ForegroundColor Yellow
}

git -C $releaseWorktree tag -f $release.tag

if ($Push) {
    git -C $releaseWorktree push origin $Branch
    git -C $releaseWorktree push origin $release.tag --force
}

if ($CreateGitHubRelease) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        throw "GitHub CLI 'gh' is required for -CreateGitHubRelease."
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        gh release view $release.tag --repo $GitHubRepositoryFullName 1>$null 2>$null
        $releaseExists = $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($releaseExists) {
        gh release upload $release.tag $zipPath $shaPath $manifest.FullName --repo $GitHubRepositoryFullName --clobber
    }
    else {
        gh release create $release.tag $zipPath $shaPath $manifest.FullName `
            --repo $GitHubRepositoryFullName `
            --title "Windows Monitor $($release.tag)" `
            --notes "Windows Monitor $($release.tag)"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Release upload failed."
    }
}

Write-Host ""
Write-Host "Release repository prepared." -ForegroundColor Green
Write-Host "Repo: $releaseWorktree"
Write-Host "Version: $($release.tag)"
if (-not $Push) {
    Write-Host "Run again with -Push to push branch and tag to $ReleaseRepository."
}
if (-not $CreateGitHubRelease) {
    Write-Host "Use -CreateGitHubRelease to upload release assets for in-app update detection."
}
