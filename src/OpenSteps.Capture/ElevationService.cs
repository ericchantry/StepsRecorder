using System.Runtime.InteropServices;

namespace OpenSteps.Capture;

/// <summary>
/// Detects administrator (elevated) processes. Windows hides input aimed at an elevated window
/// from the hooks of a non-elevated process, so those windows cannot be recorded.
/// </summary>
public sealed class ElevationService
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ErrorAccessDenied = 5;

    public bool IsCurrentProcessElevated()
    {
        return IsProcessElevated((uint)Environment.ProcessId) == true;
    }

    /// <summary>True or false when the window's process can be classified, null when it cannot.</summary>
    public bool? IsWindowElevated(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == 0 ? null : IsProcessElevated(processId);
    }

    public static bool ShouldWarn(bool currentProcessElevated, bool? windowElevated)
    {
        return !currentProcessElevated && windowElevated == true;
    }

    private static bool? IsProcessElevated(uint processId)
    {
        // A non-elevated process is denied access to a higher-privilege one, so access denied
        // means the target is elevated (or more privileged).
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == ErrorAccessDenied ? true : null;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return Marshal.GetLastWin32Error() == ErrorAccessDenied ? true : null;
            }

            try
            {
                return GetTokenInformation(token, TokenElevationClass, out var elevation, (uint)Marshal.SizeOf<TokenElevation>(), out _)
                    ? elevation.TokenIsElevated != 0
                    : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out TokenElevation tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
