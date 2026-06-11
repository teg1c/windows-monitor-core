namespace WindowsMonitor.App;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        WindowsMonitor.Infrastructure.AppPaths.EnsureDirectories();
        Application.ThreadException += (_, e) =>
            WindowsMonitor.Infrastructure.AppLogger.Error("UI 线程未处理异常。", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WindowsMonitor.Infrastructure.AppLogger.Error("应用程序未处理异常。", e.ExceptionObject as Exception);
        WindowsMonitor.Infrastructure.AppLogger.Info("应用程序启动。");
        Application.Run(new MainForm());
    }    
}
