using System.Text.RegularExpressions;

namespace OpenSteps.Core.Services;

/// <summary>
/// Decides whether typed characters must be masked. Password fields reported by UI Automation are always
/// sensitive; field names that look like secrets (PIN, CVV, card number...) are treated the same way.
/// </summary>
public static partial class SensitiveInputDetector
{
    public static bool IsSensitive(bool isPasswordField, string? name, string? automationId)
    {
        return isPasswordField || LooksSensitive(name) || LooksSensitive(automationId);
    }

    public static bool LooksSensitive(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SensitivePattern().IsMatch(value);
    }

    [GeneratedRegex(
        @"pass\s*word|passcode|passwd|passphrase|\bpwd\b|\bpin\b|secret|\bcvv2?\b|\bcvc\b|security\s*code|one[-\s]?time\s*code|\botp\b|verification\s*code|\bssn\b|social\s*security|card\s*number|credit\s*card|account\s*number|routing\s*number|api[-_\s]?key|access[-_\s]?token",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePattern();
}
