using System.Diagnostics;

namespace OpenSteps.Core.Services;

/// <summary>
/// Prints a guide HTML file to PDF with the headless Microsoft Edge (or Chrome) that is already installed on the machine.
/// </summary>
public sealed class PdfExporter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string? _browserPath;

    public PdfExporter(string? browserPath = null)
    {
        _browserPath = browserPath;
    }

    public static string? FindBrowserPath(IEnumerable<string>? candidates = null)
    {
        candidates ??= DefaultCandidates();
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string BuildArguments(string htmlPath, string pdfPath, string profileFolder)
    {
        var htmlUri = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        return $"--headless --disable-gpu --no-pdf-header-footer --user-data-dir=\"{profileFolder}\" --print-to-pdf=\"{Path.GetFullPath(pdfPath)}\" \"{htmlUri}\"";
    }

    /// <returns>null when the PDF was written, otherwise a short reason.</returns>
    public async Task<string?> ExportAsync(string htmlPath, string pdfPath, CancellationToken cancellationToken = default)
    {
        var browser = _browserPath ?? FindBrowserPath();
        if (browser is null || !File.Exists(browser))
        {
            return "Microsoft Edge or Google Chrome was not found.";
        }

        // A separate profile folder stops the browser from handing the job to an Edge window that is already open.
        var profileFolder = Path.Combine(Path.GetTempPath(), "OpenSteps", "pdf-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = browser,
                Arguments = BuildArguments(htmlPath, pdfPath, profileFolder),
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return "The browser could not be started.";
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return "The browser did not finish within 60 seconds.";
            }

            return File.Exists(pdfPath) ? null : "The browser finished without writing a PDF file.";
        }
        finally
        {
            try
            {
                if (Directory.Exists(profileFolder))
                {
                    Directory.Delete(profileFolder, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static IEnumerable<string> DefaultCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
        }

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
        }
    }
}
