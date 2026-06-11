namespace WindowsMonitor.Core.Models;

public enum MonitorScopeType
{
    AllWindows,
    Desktop,
    Process,
    WindowTitle,
    Region
}

public enum MonitorContentType
{
    WindowTitle,
    OcrText,
    TaskbarFlash
}

public enum MonitorRuleType
{
    WindowTitle,
    Ocr,
    TaskbarFlash
}

public enum OcrTargetType
{
    Desktop,
    Window
}

public enum MatchMode
{
    Contains,
    Regex,
    WholeWord
}

public enum NotificationChannel
{
    WindowsToast,
    Webhook
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    CooldownSkipped
}

public enum LicenseType
{
    Daily,
    Monthly,
    Yearly,
    Permanent
}

public enum LicenseStatus
{
    Missing,
    Valid,
    ExpiringSoon,
    Expired,
    Invalid,
    DeviceMismatch
}

public enum TaskbarFlashConfidence
{
    Low,
    Medium,
    High
}
