using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace OpenSteps.Core.Services;

/// <summary>How far a download has got, for the progress bar.</summary>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>
/// The web calls an update needs. An interface so the update logic can be tested without a network, and so the
/// whole feature can be switched off by handing over a client that does nothing.
/// </summary>
public interface IUpdateClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to GitHub over HTTPS. No sign-in is needed to read public releases; the anonymous allowance is far
/// more than one look a day. The User-Agent is required by the GitHub API.
/// </summary>
public sealed class GitHubUpdateClient : IUpdateClient, IDisposable
{
    private readonly HttpClient _http;

    public GitHubUpdateClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenSteps", "1.0"));
        }
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[81920];
        long received = 0;
        var lastReport = 0L;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (progress is not null && (received - lastReport >= 256 * 1024 || received == total))
            {
                lastReport = received;
                progress.Report(new DownloadProgress(received, total));
            }
        }

        progress?.Report(new DownloadProgress(received, total ?? received));
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Works out a file's SHA-256, to check a download arrived intact.</summary>
public static class FileChecksum
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
