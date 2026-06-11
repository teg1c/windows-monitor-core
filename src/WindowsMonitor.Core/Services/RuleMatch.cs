using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core.Services;

public sealed record RuleMatch(
    MonitorRule Rule,
    MonitorInput Input,
    string Keyword,
    string TextSnippet);
