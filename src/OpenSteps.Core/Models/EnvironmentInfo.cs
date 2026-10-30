namespace OpenSteps.Core.Models;

/// <summary>
/// System details captured when recording starts, for bug reports. Deliberately excludes
/// user name, machine name and network details.
/// </summary>
public sealed class EnvironmentInfo
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    public string? OperatingSystem { get; set; }

    public string? OsVersion { get; set; }

    public string? Architecture { get; set; }

    public string? AppVersion { get; set; }

    public string? Culture { get; set; }

    public string? KeyboardLayout { get; set; }

    public string? TimeZone { get; set; }

    public List<string> Displays { get; set; } = [];

    public IReadOnlyList<KeyValuePair<string, string>> ToRows()
    {
        var rows = new List<KeyValuePair<string, string>>();
        Add(rows, "Recorded", CapturedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Add(rows, "Operating system", OperatingSystem);
        Add(rows, "OS version", OsVersion);
        Add(rows, "Architecture", Architecture);
        Add(rows, "OpenSteps version", AppVersion);
        Add(rows, "Language / region", Culture);
        Add(rows, "Keyboard layout", KeyboardLayout);
        Add(rows, "Time zone", TimeZone);
        for (var i = 0; i < Displays.Count; i++)
        {
            Add(rows, Displays.Count == 1 ? "Display" : $"Display {i + 1}", Displays[i]);
        }

        return rows;
    }

    private static void Add(List<KeyValuePair<string, string>> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new KeyValuePair<string, string>(label, value));
        }
    }
}
