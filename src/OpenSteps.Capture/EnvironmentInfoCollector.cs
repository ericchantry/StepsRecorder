using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSteps.Core.Models;

namespace OpenSteps.Capture;

/// <summary>Collects OS, display and locale details for bug reports. Never collects user, machine or network names.</summary>
public sealed class EnvironmentInfoCollector
{
    private readonly MonitorService _monitorService;

    public EnvironmentInfoCollector(MonitorService? monitorService = null)
    {
        _monitorService = monitorService ?? new MonitorService();
    }

    public EnvironmentInfo Collect()
    {
        var info = new EnvironmentInfo
        {
            CapturedAt = DateTimeOffset.Now,
            OperatingSystem = Safe(GetOperatingSystem),
            OsVersion = Safe(() => Environment.OSVersion.Version.ToString()),
            Architecture = Safe(() => RuntimeInformation.OSArchitecture.ToString()),
            AppVersion = Safe(GetAppVersion),
            Culture = Safe(() => $"{CultureInfo.CurrentUICulture.Name} / {CultureInfo.CurrentCulture.Name}"),
            KeyboardLayout = Safe(() => System.Windows.Forms.InputLanguage.CurrentInputLanguage.LayoutName),
            TimeZone = Safe(() => TimeZoneInfo.Local.DisplayName)
        };

        try
        {
            foreach (var monitor in _monitorService.GetAllMonitors())
            {
                var scale = monitor.DpiX is > 0 ? $", {monitor.DpiX * 100 / 96}% scale" : string.Empty;
                var primary = monitor.IsPrimary ? ", primary" : string.Empty;
                info.Displays.Add($"{monitor.Width}x{monitor.Height}{scale}{primary}");
            }
        }
        catch
        {
            // Display details are best effort.
        }

        return info;
    }

    private static string? GetOperatingSystem()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string;
        var displayVersion = key?.GetValue("DisplayVersion") as string;
        var build = key?.GetValue("CurrentBuild") as string;
        var revision = key?.GetValue("UBR");
        if (product is null)
        {
            return RuntimeInformation.OSDescription;
        }

        // ProductName still says "Windows 10" on Windows 11; the build number tells them apart.
        if (int.TryParse(build, out var buildNumber) && buildNumber >= 22000)
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var buildText = build is null ? string.Empty : $" (build {build}{(revision is null ? string.Empty : $".{revision}")})";
        return $"{product}{(displayVersion is null ? string.Empty : $" {displayVersion}")}{buildText}";
    }

    private static string? GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString();
    }

    private static string? Safe(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
