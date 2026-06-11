using Microsoft.Data.Sqlite;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Infrastructure.Persistence;

namespace WindowsMonitor.Tests;

public sealed class SqliteMonitorRepositoryTests
{
    [Fact]
    public async Task GetRulesAsync_ReadsStringEnumJsonFromExistingDatabase()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"windows-monitor-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteMonitorRepository(databasePath);
            await repository.InitializeAsync();

            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE MonitorRules
                    SET ContentTypesJson = '["WindowTitle"]',
                        NotificationChannelsJson = '["WindowsToast"]';
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var rules = await repository.GetRulesAsync();

            Assert.NotEmpty(rules);
            Assert.All(rules, rule => Assert.Contains(MonitorContentType.WindowTitle, rule.ContentTypes));
            Assert.All(rules, rule => Assert.Contains(NotificationChannel.WindowsToast, rule.NotificationChannels));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task SaveRuleAsync_PreservesNotificationSettings()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"windows-monitor-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteMonitorRepository(databasePath);
            await repository.InitializeAsync();

            var rule = new MonitorRule
            {
                Name = "回调提醒",
                RuleType = MonitorRuleType.TaskbarFlash,
                ProcessName = "mhtab.exe",
                ContentTypes = [MonitorContentType.TaskbarFlash],
                Keywords = [],
                NotificationChannels = [NotificationChannel.WindowsToast, NotificationChannel.Webhook],
                WindowsToastMessageTemplate = "系统通知 {RuleName}",
                WebhookUrl = "https://open.feishu.cn/open-apis/bot/v2/hook/test",
                WebhookHeadersJson = "{\"Content-Type\":\"application/json\"}",
                WebhookBodyTemplate = "{\"msg_type\":\"text\",\"content\":{\"text\":\"掉线了\"}}"
            };

            await repository.SaveRuleAsync(rule);

            var saved = (await repository.GetRulesAsync()).Single(item => item.Id == rule.Id);
            Assert.Contains(NotificationChannel.WindowsToast, saved.NotificationChannels);
            Assert.Contains(NotificationChannel.Webhook, saved.NotificationChannels);
            Assert.Equal(rule.WebhookUrl, saved.WebhookUrl);
            Assert.Equal(rule.WebhookHeadersJson, saved.WebhookHeadersJson);
            Assert.Equal(rule.WebhookBodyTemplate, saved.WebhookBodyTemplate);
            Assert.Equal(rule.WindowsToastMessageTemplate, saved.WindowsToastMessageTemplate);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
