namespace WindowsMonitor.Core.Models;

public sealed record UpdateCheckResult(
    bool IsConfigured,
    bool HasUpdate,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    string? PackageUrl,
    string? Message);
