namespace WindowsMonitor.Core.Models;

public sealed record LicenseInfo
{
    public string LicenseId { get; init; } = string.Empty;
    public LicenseType LicenseType { get; init; } = LicenseType.Yearly;
    public string Edition { get; init; } = "Professional";
    public string DeviceHash { get; init; } = string.Empty;
    public IReadOnlyList<string> Features { get; init; } = [];
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? ExpiresAt { get; init; }
    public LicenseStatus Status { get; init; } = LicenseStatus.Missing;
    public string? RemoteMessage { get; init; }
    public DateTimeOffset? LastServerTime { get; init; }
}

public sealed record LicenseValidationResult(
    LicenseInfo? License,
    LicenseStatus Status,
    bool IsUsable,
    bool RemoteChecked,
    bool RemoteUnavailable,
    string Message);
