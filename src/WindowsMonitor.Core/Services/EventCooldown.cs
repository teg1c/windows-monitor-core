using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core.Services;

public sealed class EventCooldown
{
    private readonly Dictionary<string, DateTimeOffset> _lastHits = [];

    public NotificationStatus Evaluate(MonitorEvent monitorEvent, int cooldownSeconds)
    {
        var now = monitorEvent.OccurredAt;
        var fingerprint = monitorEvent.Fingerprint;

        if (_lastHits.TryGetValue(fingerprint, out var lastHit) &&
            now - lastHit < TimeSpan.FromSeconds(cooldownSeconds))
        {
            return NotificationStatus.CooldownSkipped;
        }

        _lastHits[fingerprint] = now;
        return NotificationStatus.Pending;
    }
}
