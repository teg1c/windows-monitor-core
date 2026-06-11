using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Capture;

public sealed class DesktopCaptureService : ICaptureService
{
    public Task<Bitmap> CaptureDesktopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = GetVirtualScreenBounds();
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return Task.FromResult(bitmap);
    }

    public Task<Bitmap?> CaptureWindowAsync(WindowSnapshot window, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            return Task.FromResult<Bitmap?>(null);
        }

        var bitmap = new Bitmap(window.Bounds.Width, window.Bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            if (PrintWindow(window.Handle, hdc, PrintWindowRenderFullContent))
            {
                return Task.FromResult<Bitmap?>(bitmap);
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        using var fallbackGraphics = Graphics.FromImage(bitmap);
        fallbackGraphics.CopyFromScreen(window.Bounds.Location, Point.Empty, window.Bounds.Size);
        return Task.FromResult<Bitmap?>(bitmap);
    }

    private static Rectangle GetVirtualScreenBounds()
    {
        var left = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
        var top = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
        var width = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
        var height = System.Windows.Forms.SystemInformation.VirtualScreen.Height;
        return new Rectangle(left, top, width, height);
    }

    private const uint PrintWindowRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
