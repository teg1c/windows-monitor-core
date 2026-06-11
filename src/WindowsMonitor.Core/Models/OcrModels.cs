namespace WindowsMonitor.Core.Models;

public sealed record OcrOptions(string Language = "zh-Hans,en", decimal MinimumConfidence = 0.8m);

public sealed record OcrResult(
    string Text,
    decimal Confidence,
    TimeSpan Duration,
    string EngineName);
