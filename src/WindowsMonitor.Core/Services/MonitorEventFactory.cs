using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core.Services;

public static class MonitorEventFactory
{
    public static MonitorEvent FromMatch(RuleMatch match, NotificationStatus status)
    {
        return new MonitorEvent
        {
            RuleId = match.Rule.Id,
            RuleName = match.Rule.Name,
            HitType = match.Input.Type,
            Keyword = match.Keyword,
            WindowTitle = match.Input.Window?.Title,
            ProcessName = match.Input.ProcessName ?? match.Input.Window?.ProcessName,
            TextSnippet = match.TextSnippet,
            EvidencePath = match.Input.EvidencePath,
            NotificationStatus = status,
            OccurredAt = match.Input.OccurredAt
        };
    }
}
