using System.Windows.Automation;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

/// <summary>
/// Cheap check of whether the keyboard focus is in a password or other sensitive field.
/// Results are cached briefly so a burst of keystrokes costs one UI Automation call.
/// Call <see cref="Invalidate"/> when focus may have moved (click, Tab, Enter...).
/// </summary>
public sealed class FocusedInputInspector
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(750);
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;
    private bool _isSensitive;

    public bool IsFocusSensitive()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _checkedAt < CacheDuration)
        {
            return _isSensitive;
        }

        _isSensitive = Inspect();
        _checkedAt = now;
        return _isSensitive;
    }

    public void Invalidate()
    {
        _checkedAt = DateTimeOffset.MinValue;
    }

    private static bool Inspect()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return false;
            }

            var current = element.Current;
            return SensitiveInputDetector.IsSensitive(current.IsPassword, current.Name, current.AutomationId);
        }
        catch
        {
            // UI Automation can fail for some apps; treat the field as ordinary rather than masking all typing.
            return false;
        }
    }
}
