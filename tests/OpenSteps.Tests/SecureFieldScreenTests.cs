using System.Diagnostics;
using System.Drawing;
using OpenSteps.Capture;
using OpenSteps.Core.Models;

namespace OpenSteps.Tests;

public sealed class SecureFieldScreenTests
{
    private const string FormTitle = "OpenSteps secure field test";

    /// <summary>
    /// Opens a real window in another process with a password box, a "Card number" box and an ordinary box, then
    /// checks that exactly the two sensitive fields are found and covered. Needs OPENSTEPS_RUN_SCREEN_TESTS=1.
    /// </summary>
    [Fact]
    public async Task FindsAndCoversPasswordAndSensitiveFieldsInAnotherApp()
    {
        if (Environment.GetEnvironmentVariable("OPENSTEPS_RUN_SCREEN_TESTS") != "1")
        {
            return;
        }

        var script = $$"""
            Add-Type -AssemblyName System.Windows.Forms
            $form = New-Object System.Windows.Forms.Form
            $form.Text = '{{FormTitle}}'
            $form.Width = 420; $form.Height = 260; $form.StartPosition = 'CenterScreen'; $form.TopMost = $true
            $password = New-Object System.Windows.Forms.TextBox
            $password.UseSystemPasswordChar = $true; $password.Text = 'hunter2'; $password.Left = 20; $password.Top = 20; $password.Width = 300
            $card = New-Object System.Windows.Forms.TextBox
            $card.AccessibleName = 'Card number'; $card.Text = '4111 1111 1111 1111'; $card.Left = 20; $card.Top = 70; $card.Width = 300
            $search = New-Object System.Windows.Forms.TextBox
            $search.AccessibleName = 'Search'; $search.Text = 'invoice 1042'; $search.Left = 20; $search.Top = 120; $search.Width = 300
            $form.Controls.AddRange(@($password, $card, $search))
            $form.Add_Shown({ $search.Focus() })
            [System.Windows.Forms.Application]::Run($form)
            """;

        var start = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-STA", "-Command", script })
        {
            start.ArgumentList.Add(argument);
        }

        using var helper = Process.Start(start)!;

        try
        {
            CapturableWindow? window = null;
            for (var i = 0; i < 100 && window is null; i++)
            {
                await Task.Delay(100);
                window = WindowEnumerator.GetCapturableWindows().FirstOrDefault(w => w.Title == FormTitle);
            }

            Assert.NotNull(window);
            await Task.Delay(500);

            var scanner = new SecureFieldScanner();
            var areas = scanner.ScanNow(window.Handle, TimeSpan.FromSeconds(5));
            var bounds = WindowEnumerator.GetVisibleBounds(window.Handle)!.Value;
            var inWindow = areas.Where(a => bounds.IntersectsWith(new Rectangle(a.X, a.Y, a.Width, a.Height))).Distinct().ToList();
            Assert.Equal(2, inWindow.Count);

            var directory = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"));
            var service = new ScreenshotService { SecureFields = scanner, SelectedWindowHandle = window.Handle };
            var shot = await service.CaptureAsync(directory, "secure.png", bounds.X + 10, bounds.Y + 10, ScreenshotMode.Window);
            Assert.Equal(2, shot.SensitiveFieldsHidden);
            Assert.True(File.Exists(shot.FilePath));
        }
        finally
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
            }
        }
    }
}
