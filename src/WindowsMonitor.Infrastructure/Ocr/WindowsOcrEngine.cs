using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Ocr;

public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly Lazy<OcrEngine?> _engine = new(CreateEngine);

    public string Name => "系统文字识别";

    public async Task<WindowsMonitor.Core.Models.OcrResult> RecognizeAsync(
        Bitmap image,
        OcrOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = _engine.Value ?? throw new InvalidOperationException("当前系统没有可用的文字识别语言包。");
        var stopwatch = Stopwatch.StartNew();

        using var ocrImage = ResizeForOcrIfNeeded(image, engine);
        using var stream = await BitmapToRandomAccessStreamAsync(ocrImage, cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);

        stopwatch.Stop();
        var text = string.Join(Environment.NewLine, result.Lines.Select(static line => line.Text));
        var confidence = string.IsNullOrWhiteSpace(text) ? 0m : 1m;
        return new WindowsMonitor.Core.Models.OcrResult(text, confidence, stopwatch.Elapsed, Name);
    }

    private static OcrEngine? CreateEngine()
    {
        foreach (var languageTag in new[] { "zh-Hans", "en-US" })
        {
            var language = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(language))
            {
                return OcrEngine.TryCreateFromLanguage(language);
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static Bitmap ResizeForOcrIfNeeded(Bitmap image, OcrEngine engine)
    {
        _ = engine;
        var max = OcrEngine.MaxImageDimension;
        if (image.Width <= max && image.Height <= max)
        {
            return new Bitmap(image);
        }

        var scale = Math.Min(max / (double)image.Width, max / (double)image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        var resized = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, 0, 0, width, height);
        return resized;
    }

    private static async Task<IRandomAccessStream> BitmapToRandomAccessStreamAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        var bytes = memory.ToArray();

        var randomAccessStream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(randomAccessStream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync().AsTask(cancellationToken);
        await writer.FlushAsync().AsTask(cancellationToken);
        writer.DetachStream();
        randomAccessStream.Seek(0);
        return randomAccessStream;
    }
}
