using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WindowsMonitor.Core;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Persistence;

public sealed class SqliteMonitorRepository(string databasePath) : IMonitorRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MonitorRules (
              Id TEXT PRIMARY KEY,
              Name TEXT NOT NULL,
              Enabled INTEGER NOT NULL,
              ScopeType TEXT NOT NULL,
              ProcessName TEXT NULL,
              WindowTitlePattern TEXT NULL,
              ContentTypesJson TEXT NOT NULL,
              KeywordsJson TEXT NOT NULL,
              MatchMode TEXT NOT NULL,
              CaseSensitive INTEGER NOT NULL,
              OcrConfidence REAL NOT NULL,
              CooldownSeconds INTEGER NOT NULL,
              NotificationChannelsJson TEXT NOT NULL,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        await EnsureColumnAsync(connection, "MonitorRules", "RuleType", "TEXT NOT NULL DEFAULT 'WindowTitle'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "OcrTargetType", "TEXT NOT NULL DEFAULT 'Desktop'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "OcrRegionX", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "OcrRegionY", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "OcrRegionWidth", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "OcrRegionHeight", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "NotificationMessageTemplate", "TEXT NOT NULL DEFAULT '规则：{RuleName}\r\n类型：{HitType}\r\n关键词：{Keyword}\r\n来源：{Source}\r\n内容：{Snippet}'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "WebhookUrl", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "WindowsToastMessageTemplate", "TEXT NOT NULL DEFAULT '规则：{RuleName}\r\n来源：{Source}\r\n内容：{Snippet}'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "WebhookHeadersJson", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "WebhookBodyTemplate", "TEXT NOT NULL DEFAULT '{\"\"ruleName\"\":\"\"{RuleName}\"\",\"\"hitType\"\":\"\"{HitType}\"\",\"\"keyword\"\":\"\"{Keyword}\"\",\"\"source\"\":\"\"{Source}\"\",\"\"snippet\"\":\"\"{Snippet}\"\",\"\"occurredAt\"\":\"\"{OccurredAt}\"\"}'", cancellationToken);
        await EnsureColumnAsync(connection, "MonitorRules", "MaxConsecutiveNotifications", "INTEGER NOT NULL DEFAULT 1", cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MonitorEvents (
              Id TEXT PRIMARY KEY,
              RuleId TEXT NULL,
              RuleName TEXT NOT NULL,
              HitType TEXT NOT NULL,
              Keyword TEXT NULL,
              WindowTitle TEXT NULL,
              ProcessName TEXT NULL,
              TextSnippet TEXT NULL,
              EvidencePath TEXT NULL,
              NotificationStatus TEXT NOT NULL,
              OccurredAt TEXT NOT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AppSettings (
              Key TEXT PRIMARY KEY,
              Value TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS LicenseState (
              Id TEXT PRIMARY KEY,
              LicenseId TEXT NOT NULL,
              LicenseType TEXT NOT NULL,
              DeviceHash TEXT NOT NULL,
              Edition TEXT NOT NULL,
              FeaturesJson TEXT NOT NULL,
              IssuedAt TEXT NOT NULL,
              ExpiresAt TEXT NULL,
              PayloadJson TEXT NOT NULL,
              Signature TEXT NOT NULL,
              ActivatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        await SeedRulesIfEmptyAsync(connection, cancellationToken);
        await RepairLegacySeedRulesAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<MonitorRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = new List<MonitorRule>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MonitorRules ORDER BY CreatedAt ASC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(ReadRule(reader));
        }

        return rules;
    }

    public async Task SaveRuleAsync(MonitorRule rule, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MonitorRules (
                Id, Name, Enabled, RuleType, ScopeType, ProcessName, WindowTitlePattern, OcrTargetType,
                OcrRegionX, OcrRegionY, OcrRegionWidth, OcrRegionHeight, ContentTypesJson,
                KeywordsJson, MatchMode, CaseSensitive, OcrConfidence, CooldownSeconds,
                MaxConsecutiveNotifications, NotificationChannelsJson, NotificationMessageTemplate,
                WindowsToastMessageTemplate, WebhookUrl, WebhookHeadersJson, WebhookBodyTemplate, CreatedAt, UpdatedAt)
            VALUES (
                $Id, $Name, $Enabled, $RuleType, $ScopeType, $ProcessName, $WindowTitlePattern, $OcrTargetType,
                $OcrRegionX, $OcrRegionY, $OcrRegionWidth, $OcrRegionHeight, $ContentTypesJson,
                $KeywordsJson, $MatchMode, $CaseSensitive, $OcrConfidence, $CooldownSeconds,
                $MaxConsecutiveNotifications, $NotificationChannelsJson, $NotificationMessageTemplate,
                $WindowsToastMessageTemplate, $WebhookUrl, $WebhookHeadersJson, $WebhookBodyTemplate, $CreatedAt, $UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Enabled = excluded.Enabled,
                RuleType = excluded.RuleType,
                ScopeType = excluded.ScopeType,
                ProcessName = excluded.ProcessName,
                WindowTitlePattern = excluded.WindowTitlePattern,
                OcrTargetType = excluded.OcrTargetType,
                OcrRegionX = excluded.OcrRegionX,
                OcrRegionY = excluded.OcrRegionY,
                OcrRegionWidth = excluded.OcrRegionWidth,
                OcrRegionHeight = excluded.OcrRegionHeight,
                ContentTypesJson = excluded.ContentTypesJson,
                KeywordsJson = excluded.KeywordsJson,
                MatchMode = excluded.MatchMode,
                CaseSensitive = excluded.CaseSensitive,
                OcrConfidence = excluded.OcrConfidence,
                CooldownSeconds = excluded.CooldownSeconds,
                MaxConsecutiveNotifications = excluded.MaxConsecutiveNotifications,
                NotificationChannelsJson = excluded.NotificationChannelsJson,
                NotificationMessageTemplate = excluded.NotificationMessageTemplate,
                WindowsToastMessageTemplate = excluded.WindowsToastMessageTemplate,
                WebhookUrl = excluded.WebhookUrl,
                WebhookHeadersJson = excluded.WebhookHeadersJson,
                WebhookBodyTemplate = excluded.WebhookBodyTemplate,
                UpdatedAt = excluded.UpdatedAt;
            """;

        AddRuleParameters(command, rule);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MonitorRules WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", ruleId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MonitorEvent>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var events = new List<MonitorEvent>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MonitorEvents ORDER BY OccurredAt DESC LIMIT $Limit;";
        command.Parameters.AddWithValue("$Limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    public async Task AddEventAsync(MonitorEvent monitorEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MonitorEvents (
                Id, RuleId, RuleName, HitType, Keyword, WindowTitle, ProcessName, TextSnippet,
                EvidencePath, NotificationStatus, OccurredAt)
            VALUES (
                $Id, $RuleId, $RuleName, $HitType, $Keyword, $WindowTitle, $ProcessName, $TextSnippet,
                $EvidencePath, $NotificationStatus, $OccurredAt);
            """;

        command.Parameters.AddWithValue("$Id", monitorEvent.Id.ToString());
        command.Parameters.AddWithValue("$RuleId", (object?)monitorEvent.RuleId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$RuleName", monitorEvent.RuleName);
        command.Parameters.AddWithValue("$HitType", monitorEvent.HitType.ToString());
        command.Parameters.AddWithValue("$Keyword", (object?)monitorEvent.Keyword ?? DBNull.Value);
        command.Parameters.AddWithValue("$WindowTitle", (object?)monitorEvent.WindowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$ProcessName", (object?)monitorEvent.ProcessName ?? DBNull.Value);
        command.Parameters.AddWithValue("$TextSnippet", (object?)monitorEvent.TextSnippet ?? DBNull.Value);
        command.Parameters.AddWithValue("$EvidencePath", (object?)monitorEvent.EvidencePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$NotificationStatus", monitorEvent.NotificationStatus.ToString());
        command.Parameters.AddWithValue("$OccurredAt", monitorEvent.OccurredAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MonitorEvents;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $Key;";
        command.Parameters.AddWithValue("$Key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value, UpdatedAt)
            VALUES ($Key, $Value, $UpdatedAt)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$Key", key);
        command.Parameters.AddWithValue("$Value", value);
        command.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedRulesIfEmptyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM MonitorRules;";
        var count = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (count > 0)
        {
            return;
        }

        foreach (var rule in SampleData.DefaultRules)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO MonitorRules (
                    Id, Name, Enabled, RuleType, ScopeType, ProcessName, WindowTitlePattern, OcrTargetType,
                    OcrRegionX, OcrRegionY, OcrRegionWidth, OcrRegionHeight, ContentTypesJson,
                    KeywordsJson, MatchMode, CaseSensitive, OcrConfidence, CooldownSeconds,
                    MaxConsecutiveNotifications, NotificationChannelsJson, NotificationMessageTemplate,
                    WindowsToastMessageTemplate, WebhookUrl, WebhookHeadersJson, WebhookBodyTemplate, CreatedAt, UpdatedAt)
                VALUES (
                    $Id, $Name, $Enabled, $RuleType, $ScopeType, $ProcessName, $WindowTitlePattern, $OcrTargetType,
                    $OcrRegionX, $OcrRegionY, $OcrRegionWidth, $OcrRegionHeight, $ContentTypesJson,
                    $KeywordsJson, $MatchMode, $CaseSensitive, $OcrConfidence, $CooldownSeconds,
                    $MaxConsecutiveNotifications, $NotificationChannelsJson, $NotificationMessageTemplate,
                    $WindowsToastMessageTemplate, $WebhookUrl, $WebhookHeadersJson, $WebhookBodyTemplate, $CreatedAt, $UpdatedAt);
                """;
            AddRuleParameters(command, rule);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task RepairLegacySeedRulesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var critical = connection.CreateCommand();
        critical.CommandText = """
            UPDATE MonitorRules
            SET Name = '关键异常提醒',
                KeywordsJson = '["失败","超时","错误","异常"]',
                UpdatedAt = $UpdatedAt
            WHERE KeywordsJson LIKE '%error%' AND KeywordsJson LIKE '%failed%' AND Name <> '关键异常提醒';
            """;
        critical.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.Now.ToString("O"));
        await critical.ExecuteNonQueryAsync(cancellationToken);

        await using var legacyTypes = connection.CreateCommand();
        legacyTypes.CommandText = """
            UPDATE MonitorRules
            SET RuleType = CASE
                WHEN ContentTypesJson LIKE '%TaskbarFlash%' THEN 'TaskbarFlash'
                WHEN ContentTypesJson LIKE '%OcrText%' AND ContentTypesJson NOT LIKE '%WindowTitle%' THEN 'Ocr'
                ELSE 'WindowTitle'
            END,
            ContentTypesJson = CASE
                WHEN ContentTypesJson LIKE '%TaskbarFlash%' THEN '["TaskbarFlash"]'
                WHEN ContentTypesJson LIKE '%OcrText%' AND ContentTypesJson NOT LIKE '%WindowTitle%' THEN '["OcrText"]'
                ELSE '["WindowTitle"]'
            END;
            """;
        await legacyTypes.ExecuteNonQueryAsync(cancellationToken);

        await using var escalation = connection.CreateCommand();
        escalation.CommandText = """
            UPDATE MonitorRules
            SET Name = '客户升级提醒',
                KeywordsJson = '["投诉","升级","紧急"]',
                UpdatedAt = $UpdatedAt
            WHERE ProcessName = 'WeChat.exe' AND Name <> '客户升级提醒';
            """;
        escalation.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.Now.ToString("O"));
        await escalation.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRuleParameters(SqliteCommand command, MonitorRule rule)
    {
        command.Parameters.AddWithValue("$Id", rule.Id.ToString());
        command.Parameters.AddWithValue("$Name", rule.Name);
        command.Parameters.AddWithValue("$Enabled", rule.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$RuleType", rule.RuleType.ToString());
        command.Parameters.AddWithValue("$ScopeType", rule.ScopeType.ToString());
        command.Parameters.AddWithValue("$ProcessName", (object?)rule.ProcessName ?? DBNull.Value);
        command.Parameters.AddWithValue("$WindowTitlePattern", (object?)rule.WindowTitlePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("$OcrTargetType", rule.OcrTargetType.ToString());
        command.Parameters.AddWithValue("$OcrRegionX", (object?)rule.OcrRegionX ?? DBNull.Value);
        command.Parameters.AddWithValue("$OcrRegionY", (object?)rule.OcrRegionY ?? DBNull.Value);
        command.Parameters.AddWithValue("$OcrRegionWidth", (object?)rule.OcrRegionWidth ?? DBNull.Value);
        command.Parameters.AddWithValue("$OcrRegionHeight", (object?)rule.OcrRegionHeight ?? DBNull.Value);
        command.Parameters.AddWithValue("$ContentTypesJson", JsonSerializer.Serialize(rule.ContentTypes, JsonOptions));
        command.Parameters.AddWithValue("$KeywordsJson", JsonSerializer.Serialize(rule.Keywords, JsonOptions));
        command.Parameters.AddWithValue("$MatchMode", rule.MatchMode.ToString());
        command.Parameters.AddWithValue("$CaseSensitive", rule.CaseSensitive ? 1 : 0);
        command.Parameters.AddWithValue("$OcrConfidence", rule.OcrConfidence);
        command.Parameters.AddWithValue("$CooldownSeconds", rule.CooldownSeconds);
        command.Parameters.AddWithValue("$MaxConsecutiveNotifications", rule.MaxConsecutiveNotifications);
        command.Parameters.AddWithValue("$NotificationChannelsJson", JsonSerializer.Serialize(rule.NotificationChannels, JsonOptions));
        command.Parameters.AddWithValue("$NotificationMessageTemplate", rule.NotificationMessageTemplate);
        command.Parameters.AddWithValue("$WindowsToastMessageTemplate", rule.WindowsToastMessageTemplate);
        command.Parameters.AddWithValue("$WebhookUrl", (object?)rule.WebhookUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$WebhookHeadersJson", rule.WebhookHeadersJson);
        command.Parameters.AddWithValue("$WebhookBodyTemplate", rule.WebhookBodyTemplate);
        command.Parameters.AddWithValue("$CreatedAt", rule.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.Now.ToString("O"));
    }

    private static MonitorRule ReadRule(SqliteDataReader reader)
    {
        return new MonitorRule
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Enabled = reader.GetInt32(reader.GetOrdinal("Enabled")) == 1,
            RuleType = Enum.Parse<MonitorRuleType>(reader.GetString(reader.GetOrdinal("RuleType"))),
            ScopeType = Enum.Parse<MonitorScopeType>(reader.GetString(reader.GetOrdinal("ScopeType"))),
            ProcessName = ReadNullableString(reader, "ProcessName"),
            WindowTitlePattern = ReadNullableString(reader, "WindowTitlePattern"),
            OcrTargetType = Enum.Parse<OcrTargetType>(reader.GetString(reader.GetOrdinal("OcrTargetType"))),
            OcrRegionX = ReadNullableInt(reader, "OcrRegionX"),
            OcrRegionY = ReadNullableInt(reader, "OcrRegionY"),
            OcrRegionWidth = ReadNullableInt(reader, "OcrRegionWidth"),
            OcrRegionHeight = ReadNullableInt(reader, "OcrRegionHeight"),
            ContentTypes = DeserializeList<MonitorContentType>(reader.GetString(reader.GetOrdinal("ContentTypesJson"))),
            Keywords = DeserializeList<string>(reader.GetString(reader.GetOrdinal("KeywordsJson"))),
            MatchMode = Enum.Parse<MatchMode>(reader.GetString(reader.GetOrdinal("MatchMode"))),
            CaseSensitive = reader.GetInt32(reader.GetOrdinal("CaseSensitive")) == 1,
            OcrConfidence = reader.GetDecimal(reader.GetOrdinal("OcrConfidence")),
            CooldownSeconds = reader.GetInt32(reader.GetOrdinal("CooldownSeconds")),
            MaxConsecutiveNotifications = reader.GetInt32(reader.GetOrdinal("MaxConsecutiveNotifications")),
            NotificationChannels = DeserializeList<NotificationChannel>(reader.GetString(reader.GetOrdinal("NotificationChannelsJson"))),
            NotificationMessageTemplate = reader.GetString(reader.GetOrdinal("NotificationMessageTemplate")),
            WindowsToastMessageTemplate = reader.GetString(reader.GetOrdinal("WindowsToastMessageTemplate")),
            WebhookUrl = ReadNullableString(reader, "WebhookUrl"),
            WebhookHeadersJson = reader.GetString(reader.GetOrdinal("WebhookHeadersJson")),
            WebhookBodyTemplate = reader.GetString(reader.GetOrdinal("WebhookBodyTemplate")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };
    }

    private static MonitorEvent ReadEvent(SqliteDataReader reader)
    {
        var ruleId = ReadNullableString(reader, "RuleId");
        return new MonitorEvent
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            RuleId = string.IsNullOrWhiteSpace(ruleId) ? null : Guid.Parse(ruleId),
            RuleName = reader.GetString(reader.GetOrdinal("RuleName")),
            HitType = Enum.Parse<MonitorContentType>(reader.GetString(reader.GetOrdinal("HitType"))),
            Keyword = ReadNullableString(reader, "Keyword"),
            WindowTitle = ReadNullableString(reader, "WindowTitle"),
            ProcessName = ReadNullableString(reader, "ProcessName"),
            TextSnippet = ReadNullableString(reader, "TextSnippet"),
            EvidencePath = ReadNullableString(reader, "EvidencePath"),
            NotificationStatus = Enum.Parse<NotificationStatus>(reader.GetString(reader.GetOrdinal("NotificationStatus"))),
            OccurredAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("OccurredAt")))
        };
    }

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
