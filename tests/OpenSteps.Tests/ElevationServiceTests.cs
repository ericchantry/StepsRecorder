using System.Diagnostics;
using OpenSteps.Capture;

namespace OpenSteps.Tests;

public sealed class ElevationServiceTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, null, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void ShouldWarn_OnlyForAdministratorWindowsWhenNotElevated(bool appElevated, bool? windowElevated, bool expected)
    {
        Assert.Equal(expected, ElevationService.ShouldWarn(appElevated, windowElevated));
    }

    [Fact]
    public void IsWindowElevated_ReturnsNullForNoWindow()
    {
        Assert.Null(new ElevationService().IsWindowElevated(IntPtr.Zero));
    }

    [Fact]
    public void IsWindowElevated_MatchesCurrentProcessForOwnWindow()
    {
        using var process = Process.GetCurrentProcess();
        var service = new ElevationService();

        // A test host may have no window, so only compare when it does.
        if (process.MainWindowHandle != IntPtr.Zero)
        {
            Assert.Equal(service.IsCurrentProcessElevated(), service.IsWindowElevated(process.MainWindowHandle));
        }
    }
}
