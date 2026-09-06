using Microsoft.JSInterop;

namespace SwLicenseWatcher.Admin.Services;

public sealed class TokenStore(IJSRuntime js)
{
    public const string StorageKey = "swlw.adminToken";

    private string? _token;
    private bool _loaded;

    public bool HasToken => !string.IsNullOrWhiteSpace(_token);

    public string? Token => _token;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _token = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
        _loaded = true;
    }

    public async Task SetAsync(string token)
    {
        _token = token;
        _loaded = true;
        await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, token);
    }

    public async Task ClearAsync()
    {
        _token = null;
        _loaded = true;
        await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
    }
}
