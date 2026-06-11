using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Tests;

public sealed class RuleMatcherTests
{
    [Fact]
    public void Match_ReturnsHit_WhenTitleContainsKeyword()
    {
        var rule = new MonitorRule
        {
            Name = "订单异常",
            Keywords = ["失败"],
            ContentTypes = [MonitorContentType.WindowTitle]
        };
        var input = new MonitorInput(
            MonitorContentType.WindowTitle,
            "同步任务失败",
            null,
            "demo.exe",
            DateTimeOffset.Now);

        var matches = new RuleMatcher().Match([rule], input).ToList();

        Assert.Single(matches);
        Assert.Equal("失败", matches[0].Keyword);
    }

    [Fact]
    public void Match_RespectsProcessFilter()
    {
        var rule = new MonitorRule
        {
            Name = "微信升级",
            ProcessName = "WeChat.exe",
            Keywords = ["升级"],
            ContentTypes = [MonitorContentType.WindowTitle]
        };
        var input = new MonitorInput(
            MonitorContentType.WindowTitle,
            "客户升级处理",
            null,
            "DingTalk.exe",
            DateTimeOffset.Now);

        var matches = new RuleMatcher().Match([rule], input);

        Assert.Empty(matches);
    }

    [Fact]
    public void EventCooldown_SkipsRepeatedFingerprintWithinWindow()
    {
        var monitorEvent = new MonitorEvent
        {
            RuleId = Guid.NewGuid(),
            RuleName = "订单异常",
            HitType = MonitorContentType.WindowTitle,
            Keyword = "失败",
            WindowTitle = "同步任务失败",
            ProcessName = "demo.exe",
            TextSnippet = "同步任务失败",
            OccurredAt = DateTimeOffset.Now
        };
        var cooldown = new EventCooldown();

        var first = cooldown.Evaluate(monitorEvent, 60);
        var second = cooldown.Evaluate(monitorEvent with { OccurredAt = monitorEvent.OccurredAt.AddSeconds(3) }, 60);

        Assert.Equal(NotificationStatus.Pending, first);
        Assert.Equal(NotificationStatus.CooldownSkipped, second);
    }
}
