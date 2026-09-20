namespace SwLicenseWatcher.Core;

public static class UserMessageText
{
    public static bool TryNormalize(
        string? title,
        string? body,
        out string normalizedTitle,
        out string normalizedBody,
        out string error)
    {
        normalizedTitle = string.Empty;
        normalizedBody = string.Empty;
        error = string.Empty;

        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0)
        {
            error = "title is required.";
            return false;
        }

        if (trimmedTitle.Length > UserMessageGrant.MaxTitleLength)
        {
            error = $"title must be at most {UserMessageGrant.MaxTitleLength} characters.";
            return false;
        }

        var trimmedBody = body?.Trim() ?? string.Empty;
        if (trimmedBody.Length == 0)
        {
            error = "body is required.";
            return false;
        }

        if (trimmedBody.Length > UserMessageGrant.MaxBodyLength)
        {
            error = $"body must be at most {UserMessageGrant.MaxBodyLength} characters.";
            return false;
        }

        normalizedTitle = trimmedTitle;
        normalizedBody = trimmedBody;
        return true;
    }
}
