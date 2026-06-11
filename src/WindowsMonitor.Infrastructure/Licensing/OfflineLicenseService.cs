using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Licensing;

public sealed class OfflineLicenseService(IMonitorRepository repository, HttpClient? httpClient = null) : ILicenseService
{
    private const string LicenseCodeKey = "license.code";
    private const string LegacyPayloadKey = "license.payload";
    private const string LegacySignatureKey = "license.signature";
    private const string LastServerTimeKey = "license.lastServerTime";
    private const string LastRemoteCheckKey = "license.lastRemoteCheck";
    private const string RemoteRevokedKey = "license.remoteRevoked";
    private const string RemoteMessageKey = "license.remoteMessage";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Development public key placeholder. Replace with production public key before release.
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEf+V8NlswimqWd3Q7LSff09Htk58Y
        sLfH/CsPRuE1kLzMtcqI0k2WnQ91E4ZRI41E6dqmAfXK7s57O9dw7nvxpQ==
        -----END PUBLIC KEY-----
        """;

    public async Task<LicenseInfo?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var code = await repository.GetSettingAsync(LicenseCodeKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var license = ParseEncryptedLicenseCode(code);
            return await AttachStoredRemoteStateAsync(license, cancellationToken);
        }

        var payloadJson = await repository.GetSettingAsync(LegacyPayloadKey, cancellationToken);
        var signature = await repository.GetSettingAsync(LegacySignatureKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(payloadJson) || string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        return await AttachStoredRemoteStateAsync(ParsePayload(payloadJson) with { Status = LicenseStatus.Valid }, cancellationToken);
    }

    public async Task<LicenseInfo> ActivateAsync(
        string licenseCode,
        string machineCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLicenseCode(licenseCode);
        var license = ParseEncryptedLicenseCode(normalized);
        EnsureMachineMatches(license, machineCode);

        await repository.SaveSettingAsync(LicenseCodeKey, normalized, cancellationToken);
        await repository.SaveSettingAsync(RemoteRevokedKey, "false", cancellationToken);
        await repository.SaveSettingAsync(RemoteMessageKey, string.Empty, cancellationToken);
        return license with { Status = LicenseStatus.Valid };
    }

    public async Task<LicenseInfo> ImportOfflineLicenseAsync(
        string filePath,
        string machineCode,
        CancellationToken cancellationToken = default)
    {
        var text = (await File.ReadAllTextAsync(filePath, cancellationToken)).Trim();
        if (text.StartsWith("WML1.", StringComparison.OrdinalIgnoreCase))
        {
            return await ActivateAsync(text, machineCode, cancellationToken);
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.TryGetProperty("licenseCode", out var licenseCodeElement))
        {
            return await ActivateAsync(licenseCodeElement.GetString() ?? string.Empty, machineCode, cancellationToken);
        }

        var payload = root.GetProperty("payload");
        var signature = root.GetProperty("signature").GetString() ?? string.Empty;
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

        if (!Verify(payloadJson, signature))
        {
            throw new InvalidOperationException("授权签名无效。");
        }

        var license = ParsePayload(payloadJson);
        EnsureMachineMatches(license, machineCode);

        await repository.SaveSettingAsync(LegacyPayloadKey, payloadJson, cancellationToken);
        await repository.SaveSettingAsync(LegacySignatureKey, signature, cancellationToken);
        return license with { Status = LicenseStatus.Valid };
    }

    public async Task<LicenseValidationResult> ValidateAsync(
        string machineCode,
        bool forceRemoteCheck = false,
        CancellationToken cancellationToken = default)
    {
        var license = await LoadAsync(cancellationToken);
        if (license is null)
        {
            return new LicenseValidationResult(null, LicenseStatus.Missing, false, false, false, "未激活授权。");
        }

        if (!string.Equals(license.DeviceHash, machineCode, StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseValidationResult(license with { Status = LicenseStatus.DeviceMismatch }, LicenseStatus.DeviceMismatch, false, false, false, "授权不属于当前机器。");
        }

        var localResult = ValidateByLocalTime(license);
        if (!localResult.IsUsable)
        {
            return localResult;
        }

        var revoked = string.Equals(await repository.GetSettingAsync(RemoteRevokedKey, cancellationToken), "true", StringComparison.OrdinalIgnoreCase);
        if (revoked)
        {
            return new LicenseValidationResult(license with { Status = LicenseStatus.Invalid }, LicenseStatus.Invalid, false, false, false, "授权已被远程服务吊销。");
        }

        var endpoint = BuildMetadata.LicenseValidationUrl;
        if (!string.IsNullOrWhiteSpace(endpoint) && (forceRemoteCheck || await ShouldRemoteCheckAsync(cancellationToken)))
        {
            var remoteResult = await TryRemoteValidateAsync(endpoint, machineCode, license, cancellationToken);
            if (remoteResult is not null)
            {
                return remoteResult;
            }
        }

        return localResult;
    }

    private async Task<LicenseValidationResult?> TryRemoteValidateAsync(
        string endpoint,
        string machineCode,
        LicenseInfo license,
        CancellationToken cancellationToken)
    {
        if (httpClient is null)
        {
            return null;
        }

        try
        {
            var licenseCode = await repository.GetSettingAsync(LicenseCodeKey, cancellationToken);
            var requestNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            using var response = await httpClient.PostAsJsonAsync(
                endpoint,
                new
                {
                    licenseCode,
                    machineCode,
                    nonce = requestNonce,
                    clientVersion = BuildVersion(),
                    product = BuildMetadata.DisplayName
                },
                JsonOptions,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var encrypted = ExtractEncryptedResponse(responseText);
            var remote = JsonSerializer.Deserialize<RemoteLicenseResponse>(
                LicenseCipher.DecryptJson(encrypted, BuildMetadata.LicenseCryptoKeyBase64),
                JsonOptions) ?? throw new InvalidOperationException("远程授权响应为空。");

            if (!string.Equals(remote.Nonce, requestNonce, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("远程授权响应随机数不匹配。");
            }

            await repository.SaveSettingAsync(LastServerTimeKey, remote.ServerUtc.ToString("O"), cancellationToken);
            await repository.SaveSettingAsync(LastRemoteCheckKey, DateTimeOffset.Now.ToString("O"), cancellationToken);
            await repository.SaveSettingAsync(RemoteMessageKey, remote.Message ?? string.Empty, cancellationToken);

            if (!remote.Valid || remote.Revoked)
            {
                await repository.SaveSettingAsync(RemoteRevokedKey, "true", cancellationToken);
                var invalid = license with { Status = LicenseStatus.Invalid, RemoteMessage = remote.Message, LastServerTime = remote.ServerUtc };
                return new LicenseValidationResult(invalid, LicenseStatus.Invalid, false, true, false, remote.Message ?? "远程服务拒绝此授权。");
            }

            await repository.SaveSettingAsync(RemoteRevokedKey, "false", cancellationToken);
            var effectiveLicense = remote.ExpiresAt is null
                ? license
                : license with { ExpiresAt = remote.ExpiresAt };
            return ValidateByServerTime(
                effectiveLicense with { LastServerTime = remote.ServerUtc, RemoteMessage = remote.Message },
                remote.ServerUtc,
                remoteUnavailable: false,
                remoteChecked: true);
        }
        catch
        {
            return null;
        }
    }

    private LicenseValidationResult ValidateWithStoredServerTime(LicenseInfo license, bool remoteUnavailable)
    {
        var serverTime = license.LastServerTime;
        if (serverTime is null)
        {
            var message = remoteUnavailable
                ? "远程校验不可用，暂时无法使用互联网时间复核授权有效期。"
                : "授权已加载，尚未完成互联网时间校验。";
            return new LicenseValidationResult(license with { Status = LicenseStatus.Valid }, LicenseStatus.Valid, true, false, remoteUnavailable, message);
        }

        return ValidateByServerTime(license, serverTime.Value, remoteUnavailable, remoteChecked: false);
    }

    private static LicenseValidationResult ValidateByLocalTime(LicenseInfo license)
    {
        var now = DateTimeOffset.Now;
        if (license.ExpiresAt is not null && now > license.ExpiresAt.Value)
        {
            var expired = license with { Status = LicenseStatus.Expired };
            return new LicenseValidationResult(expired, LicenseStatus.Expired, false, false, false, "授权已过期。");
        }

        var status = license.ExpiresAt is not null && license.ExpiresAt.Value - now <= TimeSpan.FromDays(7)
            ? LicenseStatus.ExpiringSoon
            : LicenseStatus.Valid;
        return new LicenseValidationResult(license with { Status = status }, status, true, false, false, "授权有效。");
    }

    private static LicenseValidationResult ValidateByServerTime(
        LicenseInfo license,
        DateTimeOffset serverTime,
        bool remoteUnavailable,
        bool remoteChecked)
    {
        if (license.ExpiresAt is not null && serverTime > license.ExpiresAt.Value)
        {
            var expired = license with { Status = LicenseStatus.Expired, LastServerTime = serverTime };
            return new LicenseValidationResult(expired, LicenseStatus.Expired, false, remoteChecked, remoteUnavailable, "授权已过期。");
        }

        var status = license.ExpiresAt is not null && license.ExpiresAt.Value - serverTime <= TimeSpan.FromDays(7)
            ? LicenseStatus.ExpiringSoon
            : LicenseStatus.Valid;
        return new LicenseValidationResult(license with { Status = status, LastServerTime = serverTime }, status, true, remoteChecked, remoteUnavailable, "授权有效。");
    }

    private async Task<bool> ShouldRemoteCheckAsync(CancellationToken cancellationToken)
    {
        var raw = await repository.GetSettingAsync(LastRemoteCheckKey, cancellationToken);
        return !DateTimeOffset.TryParse(raw, out var lastCheck) ||
               DateTimeOffset.Now - lastCheck >= TimeSpan.FromHours(1);
    }

    private async Task<LicenseInfo> AttachStoredRemoteStateAsync(LicenseInfo license, CancellationToken cancellationToken)
    {
        DateTimeOffset? lastServerTime = DateTimeOffset.TryParse(await repository.GetSettingAsync(LastServerTimeKey, cancellationToken), out var parsed)
            ? parsed
            : null;
        var remoteMessage = await repository.GetSettingAsync(RemoteMessageKey, cancellationToken);
        return license with { LastServerTime = lastServerTime, RemoteMessage = remoteMessage };
    }

    private static string NormalizeLicenseCode(string licenseCode)
    {
        var normalized = licenseCode.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("授权码为空。");
        }

        return normalized;
    }

    private static LicenseInfo ParseEncryptedLicenseCode(string licenseCode)
    {
        return ParsePayload(LicenseCipher.DecryptJson(NormalizeLicenseCode(licenseCode), BuildMetadata.LicenseCryptoKeyBase64));
    }

    private static void EnsureMachineMatches(LicenseInfo license, string machineCode)
    {
        if (!string.Equals(license.DeviceHash, machineCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("授权不属于当前机器。");
        }
    }

    private static string ExtractEncryptedResponse(string responseText)
    {
        if (responseText.StartsWith("WML1.", StringComparison.OrdinalIgnoreCase))
        {
            return responseText;
        }

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.TryGetProperty("response", out var responseElement)
            ? responseElement.GetString() ?? string.Empty
            : throw new InvalidOperationException("远程授权响应格式无效。");
    }

    private static bool Verify(string payloadJson, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(payloadJson),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    private static LicenseInfo ParsePayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var expiresAt = root.TryGetProperty("expiresAt", out var expiresAtElement) &&
                        expiresAtElement.ValueKind != JsonValueKind.Null
            ? expiresAtElement.GetDateTimeOffset()
            : (DateTimeOffset?)null;

        var type = root.TryGetProperty("licenseType", out var typeElement)
            ? ParseLicenseType(typeElement.GetString())
            : LicenseType.Yearly;

        var features = root.TryGetProperty("features", out var featuresElement) &&
                       featuresElement.ValueKind == JsonValueKind.Array
            ? featuresElement.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray()
            : [];

        return new LicenseInfo
        {
            LicenseId = root.GetProperty("licenseId").GetString() ?? string.Empty,
            LicenseType = type,
            Edition = root.TryGetProperty("edition", out var edition) ? edition.GetString() ?? "Professional" : "Professional",
            DeviceHash = root.GetProperty("deviceHash").GetString() ?? string.Empty,
            Features = features,
            IssuedAt = root.TryGetProperty("issuedAt", out var issuedAt) ? issuedAt.GetDateTimeOffset() : DateTimeOffset.MinValue,
            ExpiresAt = expiresAt,
            Status = LicenseStatus.Valid
        };
    }

    private static LicenseType ParseLicenseType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "daily" => LicenseType.Daily,
            "monthly" => LicenseType.Monthly,
            "yearly" => LicenseType.Yearly,
            "permanent" => LicenseType.Permanent,
            _ => LicenseType.Yearly
        };
    }

    private static string BuildVersion()
    {
        return typeof(OfflineLicenseService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private sealed record RemoteLicenseResponse(
        string Nonce,
        DateTimeOffset ServerUtc,
        bool Valid,
        bool Revoked,
        DateTimeOffset? ExpiresAt,
        string? Message);
}
