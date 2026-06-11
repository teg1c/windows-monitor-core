namespace WindowsMonitor.Infrastructure;

public static class AppPaths
{
    public static string ProgramDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsMonitor");

    public static string DataDirectory { get; } = Path.Combine(ProgramDataRoot, "data");

    public static string LogDirectory { get; } = Path.Combine(ProgramDataRoot, "logs");

    public static string UpdateDirectory { get; } = Path.Combine(ProgramDataRoot, "updates");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "app.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(UpdateDirectory);
    }
}
