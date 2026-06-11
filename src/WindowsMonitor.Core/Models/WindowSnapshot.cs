using System.Drawing;

namespace WindowsMonitor.Core.Models;

public sealed record WindowSnapshot(
    IntPtr Handle,
    string Title,
    string ClassName,
    int ProcessId,
    string ProcessName,
    Rectangle Bounds,
    bool IsVisible,
    DateTimeOffset CapturedAt);
