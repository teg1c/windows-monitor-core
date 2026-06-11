using System.Drawing;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Ocr;

public sealed class FallbackOcrEngine(IOcrEngine primary, IOcrEngine fallback) : IOcrEngine
{
    public string Name => $"{primary.Name} + {fallback.Name}";

    public async Task<OcrResult> RecognizeAsync(
        Bitmap image,
        OcrOptions options,
        CancellationToken cancellationToken = default)
    {
        OcrResult? primaryResult = null;
        try
        {
            primaryResult = await primary.RecognizeAsync(image, options, cancellationToken);
            if (!string.IsNullOrWhiteSpace(primaryResult.Text))
            {
                return primaryResult;
            }

            AppLogger.Debug($"Primary OCR returned empty text. engine={primaryResult.EngineName}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Primary OCR failed, falling back to secondary OCR. engine={primary.Name}, error={ex.Message}");
        }

        try
        {
            var fallbackResult = await fallback.RecognizeAsync(image, options, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fallbackResult.Text) || primaryResult is null)
            {
                return fallbackResult;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Fallback OCR failed. engine={fallback.Name}, error={ex.Message}");
            if (primaryResult is null)
            {
                throw;
            }
        }

        return primaryResult;
    }
}
