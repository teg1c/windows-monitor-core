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
}
