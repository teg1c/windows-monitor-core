using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core;

public static class SampleData
{
    public static IReadOnlyList<MonitorRule> DefaultRules =>
    [
        new MonitorRule
        {
            Name = "标题异常提醒",
            RuleType = MonitorRuleType.WindowTitle,
            Keywords = ["失败", "超时", "错误", "异常"],
            ContentTypes = [MonitorContentType.WindowTitle],
            NotificationChannels = [NotificationChannel.WindowsToast, NotificationChannel.Webhook],
            CooldownSeconds = 60
        },
        new MonitorRule
        {
            Name = "桌面识别异常提醒",
            RuleType = MonitorRuleType.Ocr,
            OcrTargetType = OcrTargetType.Desktop,
            Keywords = ["失败", "超时", "错误", "异常"],
            ContentTypes = [MonitorContentType.OcrText],
            NotificationChannels = [NotificationChannel.WindowsToast],
            CooldownSeconds = 60
        }
    ];
}
