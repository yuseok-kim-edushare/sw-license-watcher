namespace SwLicenseWatcher.Core;

public static class PcDisplayNames
{
    public const int MaxAssignedHostNameLength = 128;
    public const int MaxAdminNotesLength = 1024;

    public static string Resolve(string hostName, string? assignedHostName) =>
        string.IsNullOrWhiteSpace(assignedHostName) ? hostName : assignedHostName.Trim();

    public static bool TryNormalizeAssignedHostName(string? value, out string? normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            error = string.Empty;
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxAssignedHostNameLength)
        {
            normalized = null;
            error = $"assignedHostName must be at most {MaxAssignedHostNameLength} characters.";
            return false;
        }

        normalized = trimmed;
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeAdminNotes(string? value, out string? normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            error = string.Empty;
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxAdminNotesLength)
        {
            normalized = null;
            error = $"adminNotes must be at most {MaxAdminNotesLength} characters.";
            return false;
        }

        normalized = trimmed;
        error = string.Empty;
        return true;
    }
}
