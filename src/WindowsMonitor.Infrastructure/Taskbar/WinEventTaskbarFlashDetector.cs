using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;
using WindowsMonitor.Infrastructure;
using WindowsMonitor.Infrastructure.Win32;

namespace WindowsMonitor.Infrastructure.Taskbar;

public sealed class WinEventTaskbarFlashDetector : ITaskbarFlashDetector
{
    private readonly WinEventDelegate _callback;
    private IReadOnlyList<TaskbarFlashTarget> _targets = [];
    private readonly Dictionary<IntPtr, DateTimeOffset> _recentEvents = [];
    private IntPtr _alertHook;
    private IntPtr _stateHook;
    private ShellHookWindow? _shellHookWindow;

    public event EventHandler<TaskbarFlashEvent>? FlashDetected;

    public WinEventTaskbarFlashDetector()
    {
        _callback = OnWinEvent;
    }

    public void Start(IReadOnlyList<TaskbarFlashTarget> targets)
    {
        Stop();
        _targets = targets;
        AppLogger.Info($"WinEvent 任务栏闪烁检测器启动。targets={targets.Count}");
        StartShellHook();
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
        if (_shellHookWindow is not null)
        {
            _shellHookWindow.Dispose();
            _shellHookWindow = null;
            stopped = true;
        }

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
        HandleCandidateWindow(
            hwnd,
            eventType == EventSystemAlert ? TaskbarFlashConfidence.High : TaskbarFlashConfidence.Medium,
            eventType == EventSystemAlert ? "WinEvent:EVENT_SYSTEM_ALERT" : "WinEvent:EVENT_OBJECT_STATECHANGE",
            eventType);
    }

    private void HandleShellHook(IntPtr hwnd, int shellEvent)
    {
        if (shellEvent == ShellEventFlash)
        {
            HandleCandidateWindow(hwnd, TaskbarFlashConfidence.High, "ShellHook:HSHELL_FLASH", (uint)shellEvent);
        }
    }

    private void HandleCandidateWindow(
        IntPtr hwnd,
        TaskbarFlashConfidence confidence,
        string detectionMethod,
        uint eventType)
    {
        if (hwnd == IntPtr.Zero || _targets.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (_recentEvents.TryGetValue(hwnd, out var last) && now - last < TimeSpan.FromSeconds(2))
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

        _recentEvents[hwnd] = now;
        CleanupRecentEvents(now);

        AppLogger.Info($"任务栏闪烁检测器命中。process={processName}, title={title}, method={detectionMethod}, eventType=0x{eventType:X}");
        FlashDetected?.Invoke(this, new TaskbarFlashEvent(
            processName,
            title,
            hwnd,
            confidence,
            now,
            detectionMethod));
    }

    private void StartShellHook()
    {
        try
        {
            _shellHookWindow = new ShellHookWindow(this);
            _shellHookWindow.CreateHandle(new CreateParams());
            if (RegisterShellHookWindow(_shellHookWindow.Handle))
            {
                AppLogger.Info($"ShellHook 任务栏闪烁检测器已启动。message=0x{_shellHookWindow.ShellHookMessage:X}");
            }
            else
            {
                AppLogger.Warning($"ShellHook 任务栏闪烁检测器启动失败。lastError={Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ShellHook 任务栏闪烁检测器启动异常。", ex);
            _shellHookWindow?.Dispose();
            _shellHookWindow = null;
        }
    }

    private void CleanupRecentEvents(DateTimeOffset now)
    {
        foreach (var item in _recentEvents.Where(item => now - item.Value > TimeSpan.FromMinutes(2)).ToArray())
        {
            _recentEvents.Remove(item.Key);
        }
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
    private const int ShellEventFlash = 0x8006;
    private const string ShellHookMessageName = "SHELLHOOK";

    private sealed class ShellHookWindow(WinEventTaskbarFlashDetector owner) : NativeWindow, IDisposable
    {
        public int ShellHookMessage { get; } = RegisterWindowMessage(ShellHookMessageName);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ShellHookMessage)
            {
                owner.HandleShellHook(m.LParam, m.WParam.ToInt32());
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DeregisterShellHookWindow(Handle);
                DestroyHandle();
            }
        }
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string lpString);
}
