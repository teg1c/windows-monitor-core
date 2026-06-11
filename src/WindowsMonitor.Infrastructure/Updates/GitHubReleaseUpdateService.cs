using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace WindowsMonitor.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateService(HttpClient httpClient)
{
    public async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindowsMonitor", "0.1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var assets = root.GetProperty("assets")
            .EnumerateArray()
            .Select(static asset => new GitHubReleaseAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                asset.GetProperty("size").GetInt64()))
            .ToArray();

        return new GitHubReleaseInfo(
            root.GetProperty("tag_name").GetString() ?? string.Empty,
            root.GetProperty("name").GetString() ?? string.Empty,
            root.GetProperty("body").GetString() ?? string.Empty,
            root.GetProperty("prerelease").GetBoolean(),
            root.GetProperty("published_at").GetDateTimeOffset(),
            assets);
    }

    public async Task<DownloadedUpdatePackage> DownloadLatestPackageAsync(
        string owner,
        string repository,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(owner, repository, cancellationToken)
            ?? throw new InvalidOperationException("无法获取 GitHub 发布版本信息。");
        var package = release.Assets.FirstOrDefault(static asset =>
            asset.Name.StartsWith("WindowsMonitor-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(static asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("最新发布版本没有包含 zip 更新包。");

        Directory.CreateDirectory(destinationDirectory);
        var packagePath = Path.Combine(destinationDirectory, package.Name);
        await DownloadFileAsync(package.DownloadUrl, packagePath, cancellationToken);

        var shaAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, $"{package.Name}.sha256", StringComparison.OrdinalIgnoreCase));
        var sha256 = ComputeSha256(packagePath);
        var verified = false;
        if (shaAsset is not null)
        {
            var shaPath = Path.Combine(destinationDirectory, shaAsset.Name);
            await DownloadFileAsync(shaAsset.DownloadUrl, shaPath, cancellationToken);
            var expected = (await File.ReadAllTextAsync(shaPath, cancellationToken))
                .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            verified = string.Equals(expected, sha256, StringComparison.OrdinalIgnoreCase);
            if (!verified)
            {
                throw new InvalidOperationException("更新包 SHA-256 校验失败。");
            }
        }

        var sigAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, $"{package.Name}.sig", StringComparison.OrdinalIgnoreCase));
        if (sigAsset is not null)
        {
            await DownloadFileAsync(sigAsset.DownloadUrl, Path.Combine(destinationDirectory, sigAsset.Name), cancellationToken);
        }

        return new DownloadedUpdatePackage(release.TagName, packagePath, sha256, verified);
    }

    private async Task DownloadFileAsync(string url, string filePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindowsMonitor", "0.1.0"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(filePath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    string Body,
    bool Prerelease,
    DateTimeOffset PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed record GitHubReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record DownloadedUpdatePackage(string Version, string PackagePath, string Sha256, bool Sha256Verified);
