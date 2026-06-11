namespace WindowsMonitor.Core.Models;

public sealed record MonitorInput(
    MonitorContentType Type,
    string Text,
    WindowSnapshot? Window,
    string? ProcessName,
    DateTimeOffset OccurredAt,
    string? EvidencePath = null);
