namespace WindowsMonitor.Core.Models;

public sealed record MonitorEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? RuleId { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public MonitorContentType HitType { get; init; }
    public string? Keyword { get; init; }
    public string? WindowTitle { get; init; }
    public string? ProcessName { get; init; }
    public string? TextSnippet { get; init; }
    public string? EvidencePath { get; init; }
    public NotificationStatus NotificationStatus { get; init; } = NotificationStatus.Pending;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;

    public string Fingerprint =>
        $"{RuleId}:{HitType}:{ProcessName}:{WindowTitle}:{Keyword}:{TextSnippet}".ToUpperInvariant();
}
