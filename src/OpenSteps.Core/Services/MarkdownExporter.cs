using OpenSteps.Core.Models;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;

namespace OpenSteps.Core.Services;

public sealed class MarkdownExporter
{
    private readonly MarkdownBuilder _markdownBuilder;

    public MarkdownExporter(MarkdownBuilder? markdownBuilder = null)
    {
        _markdownBuilder = markdownBuilder ?? new MarkdownBuilder();
    }

    public string GetUniqueExportFolder(string parentFolder, string sessionTitle)
    {
        var safeName = ToSafeFolderName(sessionTitle);
        var candidate = Path.Combine(parentFolder, safeName);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i < 1000; i++)
        {
            candidate = Path.Combine(parentFolder, $"{safeName} ({i})");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(parentFolder, $"{safeName} ({DateTimeOffset.Now:yyyyMMdd-HHmmss})");
    }

    public string ToSafeFolderName(string title)
    {
        var safe = string.Join(string.Empty, (title ?? string.Empty).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch)).Trim();
        safe = string.Join(" ", safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length > 80)
        {
            safe = safe[..80].Trim();
        }

        return string.IsNullOrWhiteSpace(safe) ? "OpenSteps Guide" : safe;
    }

    public async Task<ExportResult> ExportAsync(RecordingSession session, string parentFolder, CancellationToken cancellationToken = default)
    {
        return await ExportAsync(session, parentFolder, GuideExportFormat.Markdown, cancellationToken);
    }

    public Task<ExportResult> ExportAsync(RecordingSession session, string parentFolder, GuideExportFormat formats, CancellationToken cancellationToken = default)
    {
        return ExportAsync(session, parentFolder, formats, GuideExportOptions.Default, cancellationToken);
    }

    public async Task<ExportResult> ExportAsync(RecordingSession session, string parentFolder, GuideExportFormat formats, GuideExportOptions options, CancellationToken cancellationToken = default)
    {
        var exportFolder = GetUniqueExportFolder(parentFolder, session.Title);
        Directory.CreateDirectory(exportFolder);
        var imageDirectory = Path.Combine(exportFolder, "images");
        Directory.CreateDirectory(imageDirectory);

        var warnings = new List<string>();
        for (var i = 0; i < session.Steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = session.Steps[i];
            var screenshotPath = GetExportScreenshotPath(step);
            if (string.IsNullOrWhiteSpace(screenshotPath))
            {
                continue;
            }

            if (!File.Exists(screenshotPath))
            {
                warnings.Add($"Step {i + 1} screenshot was missing and was skipped.");
                continue;
            }

            var destination = Path.Combine(imageDirectory, $"step-{i + 1:000}.png");
            File.Copy(screenshotPath, destination, overwrite: false);
        }

        var videoLinks = new Dictionary<int, string>();
        if (options.IncludeVideo)
        {
            foreach (var segment in session.Segments.OrderBy(s => s.Number))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(segment.VideoPath))
                {
                    continue;
                }

                if (!File.Exists(segment.VideoPath))
                {
                    warnings.Add($"Recording {segment.Number} video was missing and was skipped.");
                    continue;
                }

                var videoDirectory = Path.Combine(exportFolder, "videos");
                Directory.CreateDirectory(videoDirectory);
                var fileName = $"recording-{segment.Number}.mp4";
                File.Copy(segment.VideoPath, Path.Combine(videoDirectory, fileName), overwrite: false);
                videoLinks[segment.Number] = $"videos/{fileName}";
            }
        }

        var outputPaths = new List<string>();
        var markdownPath = string.Empty;
        var htmlPath = string.Empty;

        if (formats.HasFlag(GuideExportFormat.Markdown))
        {
            var markdown = _markdownBuilder.BuildMarkdown(session, options, videoLinks);
            markdownPath = Path.Combine(exportFolder, "guide.md");
            await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken);
            outputPaths.Add(markdownPath);
        }

        if (formats.HasFlag(GuideExportFormat.Html))
        {
            htmlPath = Path.Combine(exportFolder, "guide.html");
            await File.WriteAllTextAsync(htmlPath, BuildHtml(session, options, videoLinks), cancellationToken);
            outputPaths.Add(htmlPath);
        }

        var zipPath = string.Empty;
        if (options.CreateZip)
        {
            zipPath = GetUniqueZipPath(exportFolder);
            await Task.Run(() => ZipFile.CreateFromDirectory(exportFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true), cancellationToken);
            outputPaths.Add(zipPath);
        }

        return new ExportResult
        {
            ExportFolder = exportFolder,
            MarkdownPath = markdownPath,
            HtmlPath = htmlPath,
            ZipPath = zipPath,
            OutputPaths = outputPaths,
            Warnings = warnings
        };
    }

    private static string? GetExportScreenshotPath(RecordedStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.EditedScreenshotPath) && File.Exists(step.EditedScreenshotPath))
        {
            return step.EditedScreenshotPath;
        }

        return step.ScreenshotPath;
    }

    private static string GetUniqueZipPath(string exportFolder)
    {
        var candidate = exportFolder + ".zip";
        for (var i = 1; File.Exists(candidate) && i < 1000; i++)
        {
            candidate = $"{exportFolder} ({i}).zip";
        }

        return candidate;
    }

    private static string BuildHtml(RecordingSession session, GuideExportOptions options, IReadOnlyDictionary<int, string> videoLinks)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine($"<title>{Encode(session.Title)}</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;line-height:1.55;color:#18232b;background:#f8fcfd;margin:0;padding:40px}");
        html.AppendLine("main{max-width:1100px;margin:0 auto;background:#fff;border:1px solid #d7e7ed;border-radius:14px;padding:34px}");
        html.AppendLine("h1{margin-top:0;font-size:32px} h2{margin-top:34px;border-top:1px solid #d7e7ed;padding-top:24px}");
        html.AppendLine("img{max-width:100%;border:1px solid #d7e7ed;border-radius:10px;background:#eef7fa}");
        html.AppendLine("p{white-space:pre-wrap}");
        html.AppendLine("table{border-collapse:collapse;width:100%;font-size:14px} th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #e6eef2;vertical-align:top}");
        html.AppendLine(".env th{width:180px;color:#5a6573;font-weight:600}");
        html.AppendLine(".seek{display:inline-block;font-size:13px;padding:2px 10px;border:1px solid #c9d8e0;border-radius:999px;color:#1a2233;text-decoration:none;background:#f3f8fa;cursor:pointer}");
        html.AppendLine(".seek:hover{background:#e3eef3}");
        html.AppendLine("code{background:#f1f5f8;border-radius:4px;padding:1px 5px}");
        html.AppendLine(".rec{display:grid;grid-template-columns:minmax(0,3fr) minmax(280px,2fr);gap:18px;align-items:start;margin-bottom:24px}");
        html.AppendLine("@media (max-width:860px){.rec{grid-template-columns:1fr} body{padding:12px} main{padding:18px}}");
        html.AppendLine("video{width:100%;background:#000;border-radius:10px}");
        html.AppendLine(".timeline{max-height:520px;overflow:auto;border:1px solid #e6eef2;border-radius:10px}");
        html.AppendLine(".timeline tr[data-t]{cursor:pointer} .timeline tr[data-t]:hover{background:#f3f8fa}");
        html.AppendLine(".timeline tr.current{background:#fff4d6} .timeline .time{font-family:Consolas,monospace;color:#8a93a1;white-space:nowrap}");
        html.AppendLine(".timeline .detail{display:block;color:#8a93a1;font-size:12px} .timeline tr.step td{font-weight:600;color:#3651c9}");
        html.AppendLine(".note{color:#5a6573;font-size:13px}");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body><main>");
        html.AppendLine($"<h1>{Encode(string.IsNullOrWhiteSpace(session.Title) ? "Untitled OpenSteps Guide" : session.Title.Trim())}</h1>");
        html.AppendLine("<p>Created with OpenSteps.</p>");

        if (options.IncludeEnvironment && session.Environment is { } environment)
        {
            html.AppendLine("<h2>Environment</h2>");
            html.AppendLine("<table class=\"env\">");
            foreach (var row in environment.ToRows())
            {
                html.AppendLine($"<tr><th>{Encode(row.Key)}</th><td>{Encode(row.Value)}</td></tr>");
            }

            html.AppendLine("</table>");
        }

        for (var i = 0; i < session.Steps.Count; i++)
        {
            var step = session.Steps[i];
            var number = i + 1;
            var title = string.IsNullOrWhiteSpace(step.DisplayTitle) ? $"Step {number}" : step.DisplayTitle;
            html.AppendLine($"<h2>Step {number}: {Encode(title)}</h2>");
            if (step is { VideoSegment: { } segment, VideoOffsetSeconds: { } offset } && videoLinks.ContainsKey(segment))
            {
                html.AppendLine($"<p><a class=\"seek\" href=\"#video-{segment}\" {SeekAttributes(segment, offset)}>&#9654; Watch at {TimeFormat.Format(offset)}</a></p>");
            }

            var screenshotPath = GetExportScreenshotPath(step);
            if (!string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath))
            {
                html.AppendLine($"<p><img src=\"images/step-{number:000}.png\" alt=\"Step {number}\"></p>");
            }

            if (MarkdownBuilder.GetFullTypedText(step) is { } typed)
            {
                html.AppendLine($"<p>Text typed: <code>{Encode(typed)}</code></p>");
            }

            if (!string.IsNullOrWhiteSpace(step.UserDescription))
            {
                html.AppendLine($"<p>{Encode(step.UserDescription.Trim())}</p>");
            }
        }

        AppendHtmlRecordingSection(html, session, options, videoLinks);
        html.AppendLine("</main>");
        if (videoLinks.Count > 0)
        {
            html.AppendLine("<script>");
            html.AppendLine(SeekScript);
            html.AppendLine("</script>");
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendHtmlRecordingSection(StringBuilder html, RecordingSession session, GuideExportOptions options, IReadOnlyDictionary<int, string> videoLinks)
    {
        var entries = options.IncludeInputLog
            ? InputTimelineBuilder.BuildWithSteps(session.InputEvents, session.Steps)
            : [];
        var segments = session.Segments.Select(s => s.Number)
            .Concat(entries.Select(e => e.Segment))
            .Distinct()
            .Order()
            .ToList();
        if (segments.Count == 0 || (entries.Count == 0 && videoLinks.Count == 0))
        {
            return;
        }

        html.AppendLine("<h2>Recording</h2>");
        var note = videoLinks.Count > 0 ? "Click any row to jump to that moment in the video. " : string.Empty;
        if (session.Segments.Count > 0 && !session.Segments.Any(s => s.TypedTextCaptured))
        {
            note += "Typed characters were not recorded for this session.";
        }
        else if (entries.Count > 0)
        {
            note += "Password and other sensitive fields are masked.";
        }

        html.AppendLine($"<p class=\"note\">{Encode(note.Trim())}</p>");
        foreach (var number in segments)
        {
            var segment = session.Segments.FirstOrDefault(s => s.Number == number);
            if (segments.Count > 1)
            {
                var duration = segment is null ? string.Empty : $" ({TimeFormat.Format(segment.DurationSeconds)})";
                html.AppendLine($"<h3>Recording {number}{duration}</h3>");
            }

            var rows = entries.Where(e => e.Segment == number).ToList();
            html.AppendLine("<div class=\"rec\">");
            html.AppendLine("<div>");
            if (videoLinks.TryGetValue(number, out var link))
            {
                html.AppendLine($"<video id=\"video-{number}\" data-seg=\"{number}\" src=\"{Encode(link)}\" controls preload=\"metadata\"></video>");
            }
            else
            {
                html.AppendLine("<p class=\"note\">No video for this recording.</p>");
            }

            html.AppendLine("</div>");
            html.AppendLine($"<div class=\"timeline\" id=\"timeline-{number}\"><table>");
            foreach (var entry in rows)
            {
                var css = entry.IsStep ? " class=\"step\"" : string.Empty;
                var raw = entry.RawKeys is null ? string.Empty : $"<span class=\"detail\">Keys: {Encode(entry.RawKeys)}</span>";
                var detail = entry.Detail is null ? string.Empty : $"<span class=\"detail\">{Encode(entry.Detail)}</span>";
                html.AppendLine($"<tr{css} {SeekAttributes(number, entry.OffsetSeconds)}><td class=\"time\">{entry.TimeLabel}</td><td>{Encode(entry.Label)}{raw}{detail}</td></tr>");
            }

            html.AppendLine("</table></div>");
            html.AppendLine("</div>");
        }
    }

    private static string SeekAttributes(int segment, double offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"data-seg=\"{segment}\" data-t=\"{offset:0.###}\"");
    }

    private const string SeekScript = """
        document.querySelectorAll('[data-t]').forEach(function (el) {
          el.addEventListener('click', function (e) {
            var video = document.getElementById('video-' + el.dataset.seg);
            if (!video) { return; }
            e.preventDefault();
            video.currentTime = Math.max(0, parseFloat(el.dataset.t) - 0.3);
            if (el.tagName === 'A') { video.scrollIntoView({ behavior: 'smooth', block: 'center' }); }
            video.play();
          });
        });
        document.querySelectorAll('video[data-seg]').forEach(function (video) {
          var box = document.getElementById('timeline-' + video.dataset.seg);
          if (!box) { return; }
          var rows = Array.prototype.slice.call(box.querySelectorAll('tr[data-t]'));
          video.addEventListener('timeupdate', function () {
            var current = null;
            for (var i = 0; i < rows.length; i++) {
              if (parseFloat(rows[i].dataset.t) <= video.currentTime + 0.05) { current = rows[i]; } else { break; }
            }
            rows.forEach(function (r) { r.classList.toggle('current', r === current); });
            if (current && !video.paused) { box.scrollTop = current.offsetTop - box.clientHeight / 2; }
          });
        });
        """;

    private static string Encode(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }
}
