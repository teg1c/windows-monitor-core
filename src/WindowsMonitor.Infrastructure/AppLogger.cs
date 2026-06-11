namespace WindowsMonitor.Infrastructure;

public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class AppLogger
{
    private const long MaxLogBytes = 20L * 1024L * 1024L;
    private static readonly object SyncRoot = new();

    public static string LogPath { get; } = Path.Combine(AppPaths.LogDirectory, "app.log");
    public static string BackupLogPath { get; } = Path.Combine(AppPaths.LogDirectory, "app.1.log");

    public static void Debug(string message) => Write(AppLogLevel.Debug, message);

    public static void Info(string message) => Write(AppLogLevel.Info, message);

    public static void Warning(string message) => Write(AppLogLevel.Warning, message);

    public static void Error(string message, Exception? exception = null) => Write(AppLogLevel.Error, message, exception);

    public static string ReadRecent(int maxBytes = 512 * 1024)
    {
        lock (SyncRoot)
        {
            if (!File.Exists(LogPath))
            {
                return "暂无日志。";
            }

            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Position = start;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return start > 0 ? $"... 已省略前面的日志内容 ...{Environment.NewLine}{text}" : text;
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            File.WriteAllText(LogPath, string.Empty);
        }
    }

    private static void Write(AppLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                RotateIfNeeded();

                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
                if (exception is not null)
                {
                    line += $"{Environment.NewLine}{exception}";
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break monitoring.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaxLogBytes)
        {
            return;
        }

        if (File.Exists(BackupLogPath))
        {
            File.Delete(BackupLogPath);
        }

        File.Move(LogPath, BackupLogPath);
    }
}
