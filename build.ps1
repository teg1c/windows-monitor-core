param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$Version = "",

    [string]$LicenseValidationUrl = "",

    [string]$LicenseCryptoKeyBase64 = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY=",

    [switch]$EnableLogTab,

    [switch]$SelfContained,

    [switch]$SkipTests,

    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$solution = Join-Path $root "WindowsMonitor.slnx"
$appProject = Join-Path $root "src\WindowsMonitor.App\WindowsMonitor.App.csproj"
$updaterProject = Join-Path $root "src\WindowsMonitor.Updater\WindowsMonitor.Updater.csproj"
$versionProps = Join-Path $root "Directory.Build.props"
$distRoot = Join-Path $root "dist"
$publishDir = Join-Path $distRoot "WindowsMonitor"
$updaterDir = Join-Path $publishDir "updater"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -Path $versionProps
    $Version = $props.Project.PropertyGroup.VersionPrefix
}

$releaseVersion = $Version.Trim().TrimStart("v")
$tagName = "v$releaseVersion"
$zipName = "WindowsMonitor-$tagName-$Runtime.zip"
$zipPath = Join-Path $distRoot $zipName
$manifestPath = Join-Path $distRoot "WindowsMonitor-$tagName-manifest.json"
$latestPath = Join-Path $distRoot "latest.json"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

Write-Host "== Windows Monitor build ==" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $Runtime"
Write-Host "Version:       $tagName"
Write-Host "License URL:   $(if ([string]::IsNullOrWhiteSpace($LicenseValidationUrl)) { '(not configured)' } else { $LicenseValidationUrl })"
Write-Host "Log tab:       $([bool]$EnableLogTab)"
Write-Host "SelfContained: $([bool]$SelfContained)"
Write-Host "Output:        $publishDir"

Get-Process -Name "WindowsMonitor.App" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "Stopping running dist app: $($_.Id) $($_.Path)" -ForegroundColor Yellow
        Stop-Process -Id $_.Id -Force
        $_.WaitForExit(5000) | Out-Null
    }

if (Test-Path $distRoot) {
    Remove-Item -LiteralPath $distRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $updaterDir -Force | Out-Null

Push-Location $root
try {
    Invoke-DotNet -Arguments @("restore", $solution, "-r", $Runtime)

    if (-not $SkipTests) {
        Invoke-DotNet -Arguments @("test", $solution, "-c", $Configuration, "--no-restore")
    }

    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    $publishCommonArgs = @(
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", $selfContainedValue,
        "--no-restore",
        "/p:Version=$releaseVersion",
        "/p:AssemblyVersion=$releaseVersion.0",
        "/p:FileVersion=$releaseVersion.0",
        "/p:InformationalVersion=$tagName",
        "/p:PublishSingleFile=false",
        "/p:PublishReadyToRun=false"
    )
    if (-not [string]::IsNullOrWhiteSpace($LicenseValidationUrl)) {
        $publishCommonArgs += "/p:LicenseValidationUrl=$LicenseValidationUrl"
    }
    if (-not [string]::IsNullOrWhiteSpace($LicenseCryptoKeyBase64)) {
        $publishCommonArgs += "/p:LicenseCryptoKeyBase64=$LicenseCryptoKeyBase64"
    }
    $publishCommonArgs += "/p:EnableLogTab=$([bool]$EnableLogTab)"

    Invoke-DotNet -Arguments (@("publish", $appProject, "-o", $publishDir) + $publishCommonArgs)
    Invoke-DotNet -Arguments (@("publish", $updaterProject, "-o", $updaterDir) + $publishCommonArgs)
}
finally {
    Pop-Location
}

if (-not $NoZip) {
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    $hash = Get-FileHash -Path $zipPath -Algorithm SHA256
    "$($hash.Hash)  $zipName" |
        Set-Content -Path "$zipPath.sha256" -Encoding ASCII

    $package = Get-Item -Path $zipPath
    $manifest = [ordered]@{
        version = $releaseVersion
        tag = $tagName
        runtime = $Runtime
        package = $zipName
        sha256 = $hash.Hash.ToLowerInvariant()
        size = $package.Length
        publishedAt = (Get-Date).ToUniversalTime().ToString("o")
        repository = "teg1c/windows-monitor-release"
        notes = "Windows Monitor $tagName"
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $latestPath -Encoding UTF8
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "App:  $publishDir"
if (-not $NoZip) {
    Write-Host "Zip:  $zipPath"
    Write-Host "Hash: $zipPath.sha256"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Latest:   $latestPath"
}
