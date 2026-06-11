param(
    [Parameter(Mandatory = $true)]
    [string]$MachineCode,

    [ValidateSet("daily", "monthly", "yearly", "permanent")]
    [string]$LicenseType = "yearly",

    [int]$Days = 0,

    [string]$LicenseId = "",

    [string]$Edition = "Professional",

    [string]$KeyBase64 = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function New-RandomBytes([int]$Length) {
    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $bytes
}

function ConvertTo-HexLower([byte[]]$Bytes) {
    (($Bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

if ([string]::IsNullOrWhiteSpace($LicenseId)) {
    $LicenseId = "LIC-{0}" -f (Get-Date -Format "yyyyMMddHHmmss")
}

$issuedAt = (Get-Date).ToUniversalTime()
$expiresAt = $null
if ($LicenseType -ne "permanent") {
    $expiresAt = switch ($LicenseType) {
        "daily" { $issuedAt.AddDays([Math]::Max(1, $(if ($Days -gt 0) { $Days } else { 1 }))) }
        "monthly" { $issuedAt.AddMonths(1) }
        "yearly" { $issuedAt.AddYears(1) }
    }
}

$payload = [ordered]@{
    licenseId = $LicenseId
    licenseType = $LicenseType
    edition = $Edition
    deviceHash = $MachineCode
    features = @("window-title", "ocr", "taskbar-flash", "notifications", "updates")
    issuedAt = $issuedAt.ToString("o")
    expiresAt = if ($null -eq $expiresAt) { $null } else { $expiresAt.ToString("o") }
    nonce = ConvertTo-HexLower (New-RandomBytes 16)
}

$json = $payload | ConvertTo-Json -Depth 5 -Compress
$jsonBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
$temp = Join-Path ([IO.Path]::GetTempPath()) "windows-monitor-license-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    Set-Content -Path (Join-Path $temp "LicenseGen.csproj") -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@
    Set-Content -Path (Join-Path $temp "Program.cs") -Encoding UTF8 -Value @"
using System.Security.Cryptography;
using System.Text;

static string Base64UrlEncode(byte[] bytes)
{
    return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

var key = Convert.FromBase64String(args[0]);
var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
var nonce = RandomNumberGenerator.GetBytes(12);
var plaintext = Encoding.UTF8.GetBytes(json);
var ciphertext = new byte[plaintext.Length];
var tag = new byte[16];
using var aes = new AesGcm(key, 16);
aes.Encrypt(nonce, plaintext, ciphertext, tag);
var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
Console.WriteLine("WML1." + Base64UrlEncode(payload));
"@
    dotnet run --project (Join-Path $temp "LicenseGen.csproj") -- $KeyBase64 $jsonBase64
    if ($LASTEXITCODE -ne 0) {
        throw "License code generation failed."
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
