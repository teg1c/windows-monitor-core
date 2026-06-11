using System.IO.Compression;
using System.Security.Cryptography;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "verify" => Verify(args),
        "install" => Install(args),
        _ => Fail("未知命令。")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Verify(string[] args)
{
    var package = GetArg(args, "--package");
    var sha256 = GetArg(args, "--sha256", required: false);
    if (package is null || !File.Exists(package))
    {
        return Fail("更新包不存在。");
    }

    var actual = ComputeSha256(package);
    if (!string.IsNullOrWhiteSpace(sha256) &&
        !string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
    {
        return Fail("SHA-256 校验失败。");
    }

    Console.WriteLine(actual);
    return 0;
}

static int Install(string[] args)
{
    var package = GetArg(args, "--package");
    var target = GetArg(args, "--target");
    if (package is null || !File.Exists(package))
    {
        return Fail("更新包不存在。");
    }

    if (target is null)
    {
        return Fail("缺少 --target。");
    }

    Directory.CreateDirectory(target);
    var backup = $"{target.TrimEnd(Path.DirectorySeparatorChar)}.bak-{DateTime.Now:yyyyMMddHHmmss}";
    CopyDirectory(target, backup);

    var extract = Path.Combine(Path.GetTempPath(), $"WindowsMonitorUpdate-{Guid.NewGuid():N}");
    Directory.CreateDirectory(extract);
    ZipFile.ExtractToDirectory(package, extract, overwriteFiles: true);
    CopyDirectory(extract, target);
    Directory.Delete(extract, recursive: true);

    Console.WriteLine($"Installed. Backup: {backup}");
    return 0;
}

static string? GetArg(string[] args, string name, bool required = true)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    if (required)
    {
        throw new InvalidOperationException($"缺少参数 {name}。");
    }

    return null;
}

static string ComputeSha256(string filePath)
{
    using var stream = File.OpenRead(filePath);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static void CopyDirectory(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));
    }

    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var destination = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination, overwrite: true);
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        WindowsMonitor.Updater

        verify  --package <zip> [--sha256 <hash>]
        install --package <zip> --target <install-dir>
        """);
}
