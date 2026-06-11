using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;
using WindowsMonitor.Infrastructure;
using WindowsMonitor.Infrastructure.Win32;

namespace WindowsMonitor.Infrastructure.Taskbar;

public sealed class WinEventTaskbarFlashDetector : ITaskbarFlashDetector
{
    private readonly WinEventDelegate _callback;
    private IReadOnlyList<TaskbarFlashTarget> _targets = [];
    private IntPtr _alertHook;
    private IntPtr _stateHook;

    public event EventHandler<TaskbarFlashEvent>? FlashDetected;

    public WinEventTaskbarFlashDetector()
    {
        _callback = OnWinEvent;
    }

    public void Start(IReadOnlyList<TaskbarFlashTarget> targets)
    {
        _targets = targets;
        Stop();
        AppLogger.Info($"WinEvent 任务栏闪烁检测器启动。targets={targets.Count}");
        _alertHook = SetWinEventHook(
            EventSystemAlert,
            EventSystemAlert,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        _stateHook = SetWinEventHook(
            EventObjectStateChange,
            EventObjectStateChange,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        if (_alertHook == IntPtr.Zero)
        {
            AppLogger.Warning("WinEvent EVENT_SYSTEM_ALERT hook 创建失败。");
        }

        if (_stateHook == IntPtr.Zero)
        {
            AppLogger.Warning("WinEvent EVENT_OBJECT_STATECHANGE hook 创建失败。");
        }
    }

    public void Stop()
    {
        var stopped = false;
        if (_alertHook != IntPtr.Zero)
        {
            UnhookWinEvent(_alertHook);
            _alertHook = IntPtr.Zero;
            stopped = true;
        }

        if (_stateHook != IntPtr.Zero)
        {
            UnhookWinEvent(_stateHook);
            _stateHook = IntPtr.Zero;
            stopped = true;
        }

        if (stopped)
        {
            AppLogger.Info("WinEvent 任务栏闪烁检测器已停止。");
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || _targets.Count == 0)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        var processName = GetProcessName((int)processId);
        var target = _targets.FirstOrDefault(item =>
            string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        var title = Win32TitleReader.GetWindowTitle(hwnd);
        if (!string.IsNullOrWhiteSpace(target.WindowTitlePattern) &&
            !title.Contains(target.WindowTitlePattern, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Debug($"WinEvent 事件未命中标题过滤。process={processName}, title={title}, filter={target.WindowTitlePattern}");
            return;
        }

        AppLogger.Info($"WinEvent 任务栏闪烁事件命中。process={processName}, title={title}, eventType=0x{eventType:X}");
        FlashDetected?.Invoke(this, new TaskbarFlashEvent(
            processName,
            title,
            hwnd,
            eventType == EventSystemAlert ? TaskbarFlashConfidence.High : TaskbarFlashConfidence.Medium,
            DateTimeOffset.Now,
            eventType == EventSystemAlert ? "WinEvent:EVENT_SYSTEM_ALERT" : "WinEvent:EVENT_OBJECT_STATECHANGE"));
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{process.ProcessName}.exe";
        }
        catch
        {
            return "unknown";
        }
    }

    private const uint EventSystemAlert = 0x0002;
    private const uint EventObjectStateChange = 0x800A;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
