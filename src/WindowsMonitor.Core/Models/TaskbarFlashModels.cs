namespace WindowsMonitor.Core.Models;

public sealed record TaskbarFlashTarget(
    string ProcessName,
    string? WindowTitlePattern,
    int CooldownSeconds = 60);

public sealed record TaskbarFlashEvent(
    string ProcessName,
    string? WindowTitle,
    IntPtr? WindowHandle,
    TaskbarFlashConfidence Confidence,
    DateTimeOffset OccurredAt,
    string DetectionMethod);
