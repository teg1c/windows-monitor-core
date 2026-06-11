using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Win32;

public sealed class WindowInventoryService : IWindowInventoryService
{
    public IReadOnlyList<WindowSnapshot> GetVisibleWindows()
    {
        var windows = new List<WindowSnapshot>();
        EnumWindows((handle, lParam) =>
        {
            if (!IsCandidateWindow(handle))
            {
                return true;
            }

            var title = GetWindowText(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            var processName = GetProcessName((int)processId);
            var className = GetClassName(handle);
            GetWindowRect(handle, out var rect);

            windows.Add(new WindowSnapshot(
                handle,
                title,
                className,
                (int)processId,
                processName,
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                true,
                DateTimeOffset.Now));

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidateWindow(IntPtr handle)
    {
        if (!IsWindowVisible(handle) || GetParent(handle) != IntPtr.Zero)
        {
            return false;
        }

        var style = GetWindowLong(handle, GwlExStyle);
        return (style & WsExToolWindow) == 0;
    }

    private static string GetWindowText(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
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

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern int GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static int GetWindowLong(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
