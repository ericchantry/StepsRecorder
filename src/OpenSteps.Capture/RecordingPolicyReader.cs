using Microsoft.Win32;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

/// <summary>Reads <see cref="RecordingPolicy"/> from HKLM, then HKCU, under Software\Policies\OpenSteps.</summary>
public static class RecordingPolicyReader
{
    public static RecordingPolicy Read()
    {
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(RecordingPolicy.RegistryKeyPath);
            using var user = Registry.CurrentUser.OpenSubKey(RecordingPolicy.RegistryKeyPath);
            return RecordingPolicy.FromValues(name => ReadDword(machine, name) ?? ReadDword(user, name));
        }
        catch
        {
            return RecordingPolicy.None;
        }
    }

    private static int? ReadDword(RegistryKey? key, string name)
    {
        return key?.GetValue(name) is int value ? value : null;
    }
}
