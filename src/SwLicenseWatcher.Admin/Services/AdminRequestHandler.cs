namespace SwLicenseWatcher.Admin.Services;

public sealed class AdminRequestHandler
{
    public Func<Task>? UnauthorizedAsync { get; set; }

    public async Task<bool> RunAsync(
        Func<Task> request,
        Action<string> setError,
        string connectionError = "서버에 연결할 수 없습니다.")
    {
        try
        {
            await request();
            return true;
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            if (UnauthorizedAsync is { } unauthorized)
            {
                await unauthorized();
            }

            return false;
        }
        catch (AdminApiException ex)
        {
            setError(ex.Message);
            return false;
        }
        catch (HttpRequestException)
        {
            setError(connectionError);
            return false;
        }
    }
}
