using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WindowsMonitor.Core.Services;

namespace WindowsMonitor.Infrastructure.Licensing;

public sealed class MachineCodeService : IMachineCodeService
{
    public string GetMachineCode()
    {
        var inputs = new[]
        {
            ReadMachineGuid(),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Select(static item => item.Trim().ToUpperInvariant())
        .Order(StringComparer.Ordinal)
        .ToArray();

        var raw = string.Join("|", inputs);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hex = Convert.ToHexString(hash)[..24];
        return $"WM-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..24]}";
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
