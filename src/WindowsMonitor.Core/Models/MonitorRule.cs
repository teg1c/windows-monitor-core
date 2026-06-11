namespace WindowsMonitor.Core.Models;

public sealed record MonitorRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public MonitorRuleType RuleType { get; init; } = MonitorRuleType.WindowTitle;
    public MonitorScopeType ScopeType { get; init; } = MonitorScopeType.AllWindows;
    public string? ProcessName { get; init; }
    public string? WindowTitlePattern { get; init; }
    public OcrTargetType OcrTargetType { get; init; } = OcrTargetType.Desktop;
    public int? OcrRegionX { get; init; }
    public int? OcrRegionY { get; init; }
    public int? OcrRegionWidth { get; init; }
    public int? OcrRegionHeight { get; init; }
    public IReadOnlyList<MonitorContentType> ContentTypes { get; init; } =
        [MonitorContentType.WindowTitle];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public MatchMode MatchMode { get; init; } = MatchMode.Contains;
    public bool CaseSensitive { get; init; }
    public decimal OcrConfidence { get; init; } = 0.8m;
    public int CooldownSeconds { get; init; } = 60;
    public int MaxConsecutiveNotifications { get; init; } = 1;
    public IReadOnlyList<NotificationChannel> NotificationChannels { get; init; } =
        [NotificationChannel.WindowsToast];
    public string NotificationMessageTemplate { get; init; } =
        "规则：{RuleName}\r\n类型：{HitType}\r\n关键词：{Keyword}\r\n来源：{Source}\r\n内容：{Snippet}";
    public string WindowsToastMessageTemplate { get; init; } =
        "规则：{RuleName}\r\n来源：{Source}\r\n内容：{Snippet}";
    public string? WebhookUrl { get; init; }
    public string WebhookHeadersJson { get; init; } = "{}";
    public string WebhookBodyTemplate { get; init; } =
        "{\"ruleName\":\"{RuleName}\",\"hitType\":\"{HitType}\",\"keyword\":\"{Keyword}\",\"source\":\"{Source}\",\"snippet\":\"{Snippet}\",\"occurredAt\":\"{OccurredAt}\"}";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
