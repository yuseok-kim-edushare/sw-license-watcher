using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using SwLicenseWatcher.Admin.Components;
using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin;

public partial class App : IAsyncDisposable
{
    private const int AutoRefreshIntervalMinutes = 5;
    private const string AutoRefreshStorageKey = "swlw.adminAutoRefresh";

    [Inject] private TokenStore Tokens { get; set; } = default!;
    [Inject] private AdminApiClient Api { get; set; } = default!;
    [Inject] private AdminRequestHandler Requests { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private DetailDrawerHost Drawer { get; set; } = default!;

    private bool _ready;
    private bool _hasToken;
    private bool _autoRefresh;
    private bool _autoRefreshBusy;
    private DateTimeOffset? _lastRefreshedAt;
    private CancellationTokenSource? _autoRefreshCts;
    private string _tokenInput = "";
    private string _tokenError = "";
    private string _statusLine = "API: -";
    private string _statusClass = "status";
    private readonly DashboardTabLifetime _tabs = new();
    private DevicesTab? _devicesTab;
    private SoftwareTab? _softwareTab;
    private ViolationsTab? _violationsTab;
    private PoliciesTab? _policiesTab;
    private UninstallTab? _uninstallTab;
    private UpdatesTab? _updatesTab;

    private IAdminTab? ActiveTab => _tabs.Active switch
    {
        "devices" => _devicesTab,
        "software" => _softwareTab,
        "violations" => _violationsTab,
        "policies" => _policiesTab,
        "uninstall" => _uninstallTab,
        "updates" => _updatesTab,
        _ => null
    };

    private string AutoRefreshHint
    {
        get
        {
            if (_lastRefreshedAt is { } at)
            {
                var last = $"마지막 {at.ToLocalTime():t}";
                return _autoRefresh ? $"5분마다 · {last}" : last;
            }

            return _autoRefresh ? "5분마다" : "꺼짐";
        }
    }

    protected override void OnInitialized()
    {
        Requests.UnauthorizedAsync = HandleUnauthorizedAsync;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await Tokens.LoadAsync();
        _hasToken = Tokens.HasToken;
        _ready = true;
        await LoadAutoRefreshPreferenceAsync();
        await RefreshHealthAsync();
        if (_hasToken && _autoRefresh)
        {
            StartAutoRefreshLoop();
        }

        StateHasChanged();
    }

    private async Task OnTokenKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveTokenAsync();
        }
    }

    private async Task SaveTokenAsync()
    {
        var token = _tokenInput.Trim();
        if (string.IsNullOrEmpty(token))
        {
            _tokenError = "토큰을 입력하세요.";
            return;
        }

        await Tokens.SetAsync(token);
        _tokenInput = "";
        _tokenError = "";
        _hasToken = true;
        if (_autoRefresh)
        {
            StartAutoRefreshLoop();
        }
    }

    private async Task LogoutAsync()
    {
        StopAutoRefreshLoop();
        await Tokens.ClearAsync();
        _hasToken = false;
        _tokenError = "";
        _tabs.Reset();
        Drawer.Close();
        ClearTabReferences();
    }

    private async Task RefreshHealthAsync()
    {
        try
        {
            var origin = new Uri(Nav.BaseUri).GetLeftPart(UriPartial.Authority);
            var health = await Api.GetHealthAsync();
            var status = health?.Status ?? "Unhealthy";
            _statusLine = $"API: {origin}  ·  /health: {status}";
            _statusClass = "status " + (status == "Healthy" ? "ok" : "warn");
        }
        catch
        {
            _statusLine = "API: -  ·  /health: Unhealthy";
            _statusClass = "status warn";
        }
    }

    private void SelectTab(string tab)
    {
        _tabs.Select(tab);
    }

    private void OnRefreshed()
    {
        _lastRefreshedAt = DateTimeOffset.Now;
    }

    private async Task OnDrawerChangedAsync()
    {
        if (ActiveTab is { } tab)
        {
            await tab.RefreshAsync();
        }
    }

    private async Task HandleUnauthorizedAsync()
    {
        StopAutoRefreshLoop();
        _hasToken = false;
        _tokenError = "인증에 실패했습니다. 관리자 토큰을 다시 입력하세요.";
        _tabs.Reset();
        Drawer.Close();
        ClearTabReferences();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadAutoRefreshPreferenceAsync()
    {
        try
        {
            _autoRefresh = await Js.InvokeAsync<string?>("localStorage.getItem", AutoRefreshStorageKey) == "1";
        }
        catch (JSException)
        {
            _autoRefresh = false;
        }
    }

    private async Task ToggleAutoRefreshAsync()
    {
        _autoRefresh = !_autoRefresh;
        try
        {
            await Js.InvokeVoidAsync("localStorage.setItem", AutoRefreshStorageKey, _autoRefresh ? "1" : "0");
        }
        catch (JSException)
        {
            // Keep the in-memory preference.
        }

        if (_autoRefresh && _hasToken)
        {
            await AutoRefreshOnceAsync();
            StartAutoRefreshLoop();
            return;
        }

        StopAutoRefreshLoop();
    }

    private void StartAutoRefreshLoop()
    {
        StopAutoRefreshLoop();
        var cts = new CancellationTokenSource();
        _autoRefreshCts = cts;
        _ = RunAutoRefreshLoopAsync(cts.Token);
    }

    private void StopAutoRefreshLoop()
    {
        var cts = _autoRefreshCts;
        _autoRefreshCts = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(AutoRefreshIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await AutoRefreshOnceAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task AutoRefreshOnceAsync()
    {
        if (!_hasToken || _autoRefreshBusy)
        {
            return;
        }

        _autoRefreshBusy = true;
        try
        {
            await RefreshHealthAsync();
            if (_tabs.Active == "updates")
            {
                OnRefreshed();
            }
            else if (ActiveTab is { } activeTab)
            {
                await activeTab.RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            _autoRefreshBusy = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        Requests.UnauthorizedAsync = null;
        StopAutoRefreshLoop();
        return ValueTask.CompletedTask;
    }

    private void ClearTabReferences()
    {
        _devicesTab = null;
        _softwareTab = null;
        _violationsTab = null;
        _policiesTab = null;
        _uninstallTab = null;
        _updatesTab = null;
    }
}
