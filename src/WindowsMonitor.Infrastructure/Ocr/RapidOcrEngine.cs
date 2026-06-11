using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using RapidOcrNet;
using SkiaSharp;
using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Ocr;

public sealed class RapidOcrEngine : IOcrEngine
{
    private readonly object _syncRoot = new();
    private readonly Lazy<RapidOcr> _ocr;
    private string _modelProfile = "default";

    public RapidOcrEngine()
    {
        _ocr = new Lazy<RapidOcr>(CreateEngine);
    }

    public string Name => $"RapidOCR({_modelProfile})";

    public Task<WindowsMonitor.Core.Models.OcrResult> RecognizeAsync(
        Bitmap image,
        OcrOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        lock (_syncRoot)
        {
            using var bitmap = ToSkBitmap(image);
            var result = _ocr.Value.Detect(bitmap, RapidOcrOptions.Default with
            {
                TextScore = (float)Math.Clamp(options.MinimumConfidence, 0.1m, 0.95m),
                DoAngle = true,
                ReturnWordBox = false
            });

            stopwatch.Stop();
            var text = result.StrRes ?? string.Join(Environment.NewLine, result.TextBlocks.Select(block => block.Text));
            var confidence = CalculateConfidence(result);
            return Task.FromResult(new WindowsMonitor.Core.Models.OcrResult(text, confidence, stopwatch.Elapsed, Name));
        }
    }

    private RapidOcr CreateEngine()
    {
        var engine = new RapidOcr();
        var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        sessionOptions.InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        sessionOptions.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);

        var modelDirectory = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        var detPath = FindFirstExisting(modelDirectory, "ch_PP-OCRv5_mobile_det.onnx", "ch_PP-OCRv5_det_mobile.onnx");
        var clsPath = FindFirstExisting(modelDirectory, "ch_ppocr_mobile_v2.0_cls_infer.onnx", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        var recPath = FindFirstExisting(modelDirectory, "ch_PP-OCRv5_rec_mobile.onnx", "ch_PP-OCRv5_rec_mobile_infer.onnx");
        var keysPath = FindFirstExisting(modelDirectory, "ppocrv5_dict.txt", "ch_PP-OCRv5_rec_mobile_dict.txt");

        if (detPath is not null && clsPath is not null && recPath is not null && keysPath is not null)
        {
            engine.InitModels(detPath, clsPath, recPath, keysPath, sessionOptions);
            _modelProfile = "PP-OCRv5-ch";
            AppLogger.Info($"RapidOCR initialized with Chinese models. modelDirectory={modelDirectory}");
            return engine;
        }

        engine.InitModels(sessionOptions);
        _modelProfile = "PP-OCRv5-latin";
        AppLogger.Warning($"RapidOCR Chinese models not found, using bundled Latin models. modelDirectory={modelDirectory}");
        return engine;
    }

    private static string? FindFirstExisting(string directory, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static SKBitmap ToSkBitmap(Bitmap image)
    {
        using var memory = new MemoryStream();
        image.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        return SKBitmap.Decode(memory)
            ?? throw new InvalidOperationException("RapidOCR could not decode the captured image.");
    }

    private static decimal CalculateConfidence(RapidOcrNet.OcrResult result)
    {
        var scores = result.TextBlocks
            .SelectMany(block => block.CharScores ?? [])
            .Where(score => !float.IsNaN(score) && score > 0)
            .ToArray();

        if (scores.Length == 0)
        {
            return string.IsNullOrWhiteSpace(result.StrRes) ? 0m : 1m;
        }

        return (decimal)scores.Average();
    }
}
