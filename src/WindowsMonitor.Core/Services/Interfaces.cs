using WindowsMonitor.Core.Models;

namespace WindowsMonitor.Core.Services;

public interface IMonitorRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitorRule>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task SaveRuleAsync(MonitorRule rule, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitorEvent>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken = default);
    Task AddEventAsync(MonitorEvent monitorEvent, CancellationToken cancellationToken = default);
    Task ClearEventsAsync(CancellationToken cancellationToken = default);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default);
}

public interface IWindowInventoryService
{
    IReadOnlyList<WindowSnapshot> GetVisibleWindows();
}

public interface IMachineCodeService
{
    string GetMachineCode();
}

public interface ICaptureService
{
    Task<System.Drawing.Bitmap> CaptureDesktopAsync(CancellationToken cancellationToken = default);
    Task<System.Drawing.Bitmap?> CaptureWindowAsync(WindowSnapshot window, CancellationToken cancellationToken = default);
}

public interface IOcrEngine
{
    string Name { get; }
    Task<OcrResult> RecognizeAsync(System.Drawing.Bitmap image, OcrOptions options, CancellationToken cancellationToken = default);
}

public interface INotificationSender
{
    Task SendAsync(MonitorEvent monitorEvent, IReadOnlyList<NotificationChannel> channels, CancellationToken cancellationToken = default);
}

public interface ILicenseService
{
    Task<LicenseInfo?> LoadAsync(CancellationToken cancellationToken = default);
    Task<LicenseInfo> ActivateAsync(string licenseCode, string machineCode, CancellationToken cancellationToken = default);
    Task<LicenseInfo> ImportOfflineLicenseAsync(string filePath, string machineCode, CancellationToken cancellationToken = default);
    Task<LicenseValidationResult> ValidateAsync(string machineCode, bool forceRemoteCheck = false, CancellationToken cancellationToken = default);
}

public interface ITaskbarFlashDetector : IDisposable
{
    event EventHandler<TaskbarFlashEvent>? FlashDetected;
    void Start(IReadOnlyList<TaskbarFlashTarget> targets);
    void Stop();
}
