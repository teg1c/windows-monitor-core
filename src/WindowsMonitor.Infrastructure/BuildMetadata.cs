using System.Reflection;

namespace WindowsMonitor.Infrastructure;

public static class BuildMetadata
{
    public static string BrandChineseName { get; } = Metadata("BrandChineseName", "窗巡");
    public static string BrandEnglishName { get; } = Metadata("BrandEnglishName", "Window Sentinel");
    public static string DisplayName { get; } = $"{BrandChineseName} {BrandEnglishName}";
    public static string LicenseValidationUrl { get; } = Metadata("LicenseValidationUrl", string.Empty);
    public static bool EnableLogTab { get; } = bool.TryParse(Metadata("EnableLogTab", "false"), out var value) && value;
    public static string LicenseCryptoKeyBase64 { get; } = Metadata(
        "LicenseCryptoKeyBase64",
        "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY=");

    private static string Metadata(string key, string fallback)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value is { Length: > 0 } value
            ? value
            : fallback;
    }
}
