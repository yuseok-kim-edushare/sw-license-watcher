using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using SwLicenseWatcher.Admin.Models;
using SwLicenseWatcher.Admin.Services;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Admin;

public partial class App : IAsyncDisposable
{
    private const int PageSize = 50;
    private const int AutoRefreshIntervalMinutes = 5;
    private const string AutoRefreshStorageKey = "swlw.adminAutoRefresh";
    internal static readonly string[] SoftwareClasses = ["", "white", "managed", "black", "unclassified"];
    internal static readonly string[] PolicyClasses = ["", "white", "managed", "black"];

    [Inject] private TokenStore Tokens { get; set; } = default!;
    [Inject] private AdminApiClient Api { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

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
    private string _tab = "devices";
    private int _skip;
    private int _totalCount;
    private string _search = "";
    private string _staleHours = "";
    private string _classFilter = "";
    private string _since = "";
    private string _listError = "";
    private string _listMeta = "";
    private string _policyError = "";
    private string _updateError = "";
    private string _updateSaved = "";
    private string _drawerError = "";
    private string _bulkError = "";

    private IReadOnlyList<DeviceSummary> _devices = [];
    private IReadOnlyList<SoftwareAggregate> _software = [];
    private IReadOnlyList<SoftwareViolationEntry> _violations = [];
    private IReadOnlyList<SoftwarePolicyEntry> _policies = [];
    private IReadOnlyList<AdminUninstallRequest> _uninstalls = [];

    private readonly HashSet<string> _selectedSoftwareKeys = [];
    private string _bulkClassification = "managed";
    private string _bulkLicense = "";

    private long? _policyId;
    private string _policyProduct = "";
    private string _policyPublisher = "";
    private string _policyVersion = "";
    private string _policyClassification = "black";
    private string _policyLicense = "";
    private string _policyNotes = "";
    private bool _policyEnabled = true;

    private string _updateTarget = "";
    private string _updateVersion = "";
    private string _updatePackageUrl = "";
    private string _updateSha256 = "";
    private bool _updateRequireAuthenticode = true;
    private int _updateRollbackMinutes = 10;

    private string? _drawerKind;
    private string _drawerKey = "";
    private string _drawerTitle = "상세";
    private string _drawerClassification = "";
    private string _drawerPublisher = "";
    private string _drawerClassFilter = "";
    private string _drawerClassifyAs = "managed";
    private string _drawerLicense = "";
    private DeviceDetail? _drawerDevice;
    private IReadOnlyList<SoftwareDevice> _drawerSoftwareDevices = [];
    private int _drawerSoftwareTotal;

    private bool ShowStaleFilter => _tab == "devices";
    private bool ShowClassFilter => _tab is "software" or "policies";
    private bool ShowSinceFilter => _tab == "violations";
    private bool ShowPolicyForm => _tab == "policies";
    private bool ShowUninstallHint => _tab == "uninstall";
    private bool ShowCsv => _tab is not "uninstall" and not "updates";
    private bool ShowListToolbar => _tab != "updates";
    private bool ShowBulkBar => _tab == "software";
    private bool ShowManagedLicense => _bulkClassification == "managed";
    private bool ShowPolicyLicense => _policyClassification == "managed";
    private bool ShowDrawerClassifyLicense => _drawerClassifyAs == "managed";
    private string[] ClassFilterValues => _tab == "policies" ? PolicyClasses : SoftwareClasses;
    private int PageFrom => _totalCount == 0 ? 0 : _skip + 1;
    private int PageTo => Math.Min(_skip + PageSize, _totalCount);
    private bool CanPrev => _skip > 0;
    private bool CanNext => _skip + PageSize < _totalCount;
    private bool AllPageSelected =>
        _software.Count > 0 && _software.All(row => _selectedSoftwareKeys.Contains(SoftwareKey(row)));
    private int SelectedNameCount =>
        _software.Where(row => _selectedSoftwareKeys.Contains(SoftwareKey(row)))
            .Select(row => row.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
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
        if (_hasToken)
        {
            await LoadListAsync();
            if (_autoRefresh)
            {
                StartAutoRefreshLoop();
            }
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
        await LoadListAsync();
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
        CloseDrawer();
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

    private async Task SelectTabAsync(string tab)
    {
        if (_tab == tab)
        {
            return;
        }

        _tab = tab;
        _skip = 0;
        _listError = "";
        _selectedSoftwareKeys.Clear();
        CloseDrawer();
        if (tab == "policies")
        {
            ResetPolicyForm();
        }

        if (!_hasToken)
        {
            return;
        }

        if (tab == "updates")
        {
            await LoadWorkerUpdatePinAsync();
            return;
        }

        await LoadListAsync();
    }

    private async Task QueryAsync()
    {
        _skip = 0;
        await LoadListAsync();
    }

    private async Task PrevPageAsync()
    {
        _skip = Math.Max(0, _skip - PageSize);
        await LoadListAsync();
    }

    private async Task NextPageAsync()
    {
        _skip += PageSize;
        await LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        _listError = "";
        _listMeta = "불러오는 중...";
        try
        {
            var query = BuildListQuery();
            switch (_tab)
            {
                case "devices":
                    var devices = await Api.GetDevicesAsync(query);
                    _devices = devices.Items ?? [];
                    _totalCount = devices.TotalCount;
                    break;
                case "software":
                    var software = await Api.GetSoftwareAsync(query);
                    _software = software.Items ?? [];
                    _totalCount = software.TotalCount;
                    PruneSoftwareSelection();
                    break;
                case "violations":
                    var violations = await Api.GetViolationsAsync(query);
                    _violations = violations.Items ?? [];
                    _totalCount = violations.TotalCount;
                    break;
                case "policies":
                    var policies = await Api.GetPoliciesAsync(query);
                    _policies = policies.Items ?? [];
                    _totalCount = policies.TotalCount;
                    break;
                case "uninstall":
                    var uninstalls = await Api.GetUninstallRequestsAsync(query);
                    _uninstalls = uninstalls.Items ?? [];
                    _totalCount = uninstalls.TotalCount;
                    break;
                default:
                    return;
            }

            _listMeta = $"총 {_totalCount}건";
            _lastRefreshedAt = DateTimeOffset.Now;
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _listError = ex.Message;
            _listMeta = "";
        }
        catch (HttpRequestException)
        {
            _listError = "서버에 연결할 수 없습니다.";
            _listMeta = "";
        }
    }

    private async Task OnSearchKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await QueryAsync();
        }
    }

    private async Task DownloadCsvAsync()
    {
        _listError = "";
        try
        {
            var (path, name) = _tab switch
            {
                "devices" => ("/api/inventory/devices", "devices.csv"),
                "software" => ("/api/inventory/software", "software.csv"),
                "violations" => ("/api/violations", "violations.csv"),
                "policies" => ("/api/policies", "policies.csv"),
                _ => ("", "")
            };
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var query = BuildListQuery(includePaging: false);
            query += (query.Length == 0 ? "" : "&") + "format=csv";
            await Api.DownloadCsvAsync($"{path}?{query}", name);
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _listError = ex.Message;
        }
        catch (HttpRequestException)
        {
            _listError = "CSV를 받지 못했습니다.";
        }
    }

    private string BuildListQuery(bool includePaging = true)
    {
        var parts = new List<string>();
        if (includePaging)
        {
            parts.Add($"skip={_skip}");
            parts.Add($"take={PageSize}");
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            parts.Add("search=" + Uri.EscapeDataString(_search.Trim()));
        }

        if (_tab == "devices" && !string.IsNullOrWhiteSpace(_staleHours))
        {
            parts.Add("staleAfterHours=" + Uri.EscapeDataString(_staleHours.Trim()));
        }

        if ((_tab == "software" || _tab == "policies") && !string.IsNullOrWhiteSpace(_classFilter))
        {
            parts.Add("classification=" + Uri.EscapeDataString(_classFilter));
        }

        if (_tab == "violations" && !string.IsNullOrWhiteSpace(_since))
        {
            parts.Add("since=" + Uri.EscapeDataString(_since));
        }

        return string.Join("&", parts);
    }

    internal static string SoftwareKey(SoftwareAggregate row) =>
        $"{row.Name}\u001f{row.Version}\u001f{row.Classification}";

    private void ToggleSelectAll(ChangeEventArgs args)
    {
        var selected = args.Value is true;
        foreach (var row in _software)
        {
            if (selected)
            {
                _selectedSoftwareKeys.Add(SoftwareKey(row));
            }
            else
            {
                _selectedSoftwareKeys.Remove(SoftwareKey(row));
            }
        }
    }

    private void ToggleSoftwareRow(SoftwareAggregate row, ChangeEventArgs args)
    {
        var key = SoftwareKey(row);
        if (args.Value is true)
        {
            _selectedSoftwareKeys.Add(key);
        }
        else
        {
            _selectedSoftwareKeys.Remove(key);
        }
    }

    private void PruneSoftwareSelection()
    {
        var pageKeys = _software.Select(SoftwareKey).ToHashSet(StringComparer.Ordinal);
        _selectedSoftwareKeys.RemoveWhere(key => !pageKeys.Contains(key));
    }

    private async Task ApplyBulkClassificationAsync()
    {
        _bulkError = "";
        var names = _software
            .Where(row => _selectedSoftwareKeys.Contains(SoftwareKey(row)))
            .Select(row => row.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
        {
            _bulkError = "적용할 소프트웨어를 선택하세요.";
            return;
        }

        if (!TryParseClassification(_bulkClassification, out var classification))
        {
            _bulkError = "분류를 선택하세요.";
            return;
        }

        var items = names.Select(name => new SoftwareClassificationItemWriteRequest(
            name,
            classification,
            Publisher: null,
            DefaultLicenseSource: classification == SoftwarePolicyClassification.Managed
                ? EmptyToNull(_bulkLicense)
                : null)).ToList();

        try
        {
            await Api.PutClassificationsAsync(new SoftwareClassificationBatchWriteRequest(items));
            _selectedSoftwareKeys.Clear();
            await LoadListAsync();
            if (_drawerKind == "software")
            {
                await LoadDrawerAsync();
            }
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _bulkError = ex.Message;
        }
    }

    private async Task OpenDeviceAsync(string deviceCode)
    {
        _drawerKind = "device";
        _drawerKey = deviceCode;
        _drawerTitle = deviceCode;
        _drawerClassFilter = "";
        _drawerError = "";
        await LoadDrawerAsync();
    }

    private async Task OpenSoftwareAsync(SoftwareAggregate row)
    {
        _drawerKind = "software";
        _drawerKey = row.Name;
        _drawerTitle = row.Name;
        _drawerClassification = row.Classification;
        _drawerPublisher = "";
        _drawerClassFilter = "";
        _drawerClassifyAs = row.Classification is "white" or "managed" or "black" ? row.Classification : "managed";
        _drawerLicense = "";
        _drawerError = "";
        await LoadDrawerAsync();
        await LoadSoftwarePolicyDefaultAsync(row.Name);
    }

    private async Task LoadDrawerAsync()
    {
        if (_drawerKind is null)
        {
            return;
        }

        _drawerError = "";
        try
        {
            if (_drawerKind == "device")
            {
                _drawerDevice = await Api.GetDeviceAsync(_drawerKey, EmptyToNull(_drawerClassFilter));
            }
            else
            {
                var query = "take=100";
                if (!string.IsNullOrEmpty(_drawerClassFilter))
                {
                    query += "&classification=" + Uri.EscapeDataString(_drawerClassFilter);
                }

                var data = await Api.GetSoftwareDevicesAsync(_drawerKey, query);
                _drawerSoftwareDevices = data.Items ?? [];
                _drawerSoftwareTotal = data.TotalCount;
                if (string.IsNullOrEmpty(_drawerPublisher) && _drawerSoftwareDevices.Count > 0)
                {
                    _drawerPublisher = _drawerSoftwareDevices[0].Publisher ?? "";
                }
            }
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _drawerError = ex.Message;
        }
    }

    private async Task LoadSoftwarePolicyDefaultAsync(string name)
    {
        try
        {
            var data = await Api.GetPoliciesAsync("search=" + Uri.EscapeDataString(name) + "&take=20");
            var match = data.Items?.FirstOrDefault(row =>
                string.Equals(row.ProductName, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _drawerLicense = match.DefaultLicenseSource ?? "";
            }
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException)
        {
            /* keep unset default */
        }
    }

    private async Task SaveDrawerClassificationAsync()
    {
        _drawerError = "";
        if (!TryParseClassification(_drawerClassifyAs, out var classification))
        {
            _drawerError = "분류를 선택하세요.";
            return;
        }

        try
        {
            await Api.PutClassificationAsync(_drawerKey, new SoftwareClassificationWriteRequest(
                classification,
                EmptyToNull(_drawerPublisher),
                classification == SoftwarePolicyClassification.Managed ? EmptyToNull(_drawerLicense) : null));
            _drawerClassification = _drawerClassifyAs;
            await LoadListAsync();
            await LoadDrawerAsync();
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _drawerError = ex.Message;
        }
    }

    private async Task SaveLicenseSourceAsync(string deviceCode, string softwareName, string? licenseSource)
    {
        _drawerError = "";
        try
        {
            await Api.PutLicenseSourceAsync(deviceCode, softwareName, new DeviceSoftwareLicenseSourceWriteRequest(EmptyToNull(licenseSource)));
            await LoadListAsync();
            await LoadDrawerAsync();
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _drawerError = ex.Message;
        }
    }

    private void CloseDrawer()
    {
        _drawerKind = null;
        _drawerKey = "";
        _drawerDevice = null;
        _drawerSoftwareDevices = [];
        _drawerError = "";
    }

    private void FillPolicyForm(SoftwarePolicyEntry? row)
    {
        _policyError = "";
        if (row is null)
        {
            ResetPolicyForm();
            return;
        }

        _policyId = row.Id;
        _policyProduct = row.ProductName;
        _policyPublisher = row.Publisher ?? "";
        _policyVersion = row.VersionPattern ?? "";
        _policyClassification = ClassifyStorage(row.Classification);
        _policyLicense = row.DefaultLicenseSource ?? "";
        _policyNotes = row.Notes ?? "";
        _policyEnabled = row.Enabled;
    }

    private void ResetPolicyForm()
    {
        _policyId = null;
        _policyProduct = "";
        _policyPublisher = "";
        _policyVersion = "";
        _policyClassification = "black";
        _policyLicense = "";
        _policyNotes = "";
        _policyEnabled = true;
        _policyError = "";
    }

    private void ApplyWorkerUpdatePin(UpdateManifest pin)
    {
        _updateTarget = pin.TargetServiceName;
        _updateVersion = pin.Version;
        _updatePackageUrl = pin.PackageUrl;
        _updateSha256 = pin.Sha256;
        _updateRequireAuthenticode = pin.RequireAuthenticode;
        _updateRollbackMinutes = pin.RollbackAfterMinutes;
    }

    private async Task LoadWorkerUpdatePinAsync()
    {
        _updateError = "";
        _updateSaved = "";
        try
        {
            ApplyWorkerUpdatePin(await Api.GetWorkerUpdatePinAsync());
            _lastRefreshedAt = DateTimeOffset.Now;
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _updateError = ex.Message;
        }
    }

    private async Task SaveWorkerUpdatePinAsync()
    {
        _updateError = "";
        _updateSaved = "";
        var request = new UpdateManifest(
            string.IsNullOrWhiteSpace(_updateTarget) ? "SwLicenseWatcher.Agent.Worker" : _updateTarget.Trim(),
            _updateVersion.Trim(),
            _updatePackageUrl.Trim(),
            _updateSha256.Trim(),
            _updateRequireAuthenticode,
            _updateRollbackMinutes);
        try
        {
            var saved = await Api.PutWorkerUpdatePinAsync(request);
            ApplyWorkerUpdatePin(saved);
            _updateSaved = $"핀이 {saved.Version}으로 저장되었습니다. Watchdog는 다음 확인 주기에 따라갑니다.";
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _updateError = ex.Message;
        }
    }

    private async Task SavePolicyAsync()
    {
        _policyError = "";
        if (!TryParseClassification(_policyClassification, out var classification))
        {
            _policyError = "분류를 선택하세요.";
            return;
        }

        var request = new SoftwarePolicyWriteRequest(
            _policyProduct.Trim(),
            EmptyToNull(_policyPublisher),
            EmptyToNull(_policyVersion),
            classification,
            EmptyToNull(_policyNotes),
            _policyEnabled,
            classification == SoftwarePolicyClassification.Managed ? EmptyToNull(_policyLicense) : null);

        try
        {
            if (_policyId is { } id)
            {
                await Api.UpdatePolicyAsync(id, request);
            }
            else
            {
                await Api.CreatePolicyAsync(request);
            }

            ResetPolicyForm();
            await LoadListAsync();
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _policyError = ex.Message;
        }
    }

    private async Task DeletePolicyAsync()
    {
        if (_policyId is not { } id)
        {
            return;
        }

        if (!await Js.InvokeAsync<bool>("confirm", "이 정책을 삭제할까요?"))
        {
            return;
        }

        _policyError = "";
        try
        {
            await Api.DeletePolicyAsync(id);
            ResetPolicyForm();
            await LoadListAsync();
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _policyError = ex.Message;
        }
    }

    private async Task DecideUninstallAsync(long id, bool approve)
    {
        _listError = "";
        try
        {
            if (approve)
            {
                await Api.ApproveUninstallAsync(id);
            }
            else
            {
                await Api.DenyUninstallAsync(id);
            }

            await LoadListAsync();
        }
        catch (AdminApiException ex) when (ex.IsUnauthorized)
        {
            await HandleUnauthorizedAsync();
        }
        catch (AdminApiException ex)
        {
            _listError = ex.Message;
        }
    }

    private async Task HandleUnauthorizedAsync()
    {
        StopAutoRefreshLoop();
        _hasToken = false;
        _tokenError = "인증에 실패했습니다. 관리자 토큰을 다시 입력하세요.";
        CloseDrawer();
        await Task.CompletedTask;
    }

    private async Task LoadAutoRefreshPreferenceAsync()
    {
        try
        {
            var stored = await Js.InvokeAsync<string?>("localStorage.getItem", AutoRefreshStorageKey);
            _autoRefresh = stored == "1";
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
            /* keep in-memory toggle */
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
            if (_tab == "updates")
            {
                _lastRefreshedAt = DateTimeOffset.Now;
                return;
            }

            await LoadListAsync();
            if (_drawerKind is not null)
            {
                await LoadDrawerAsync();
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
        StopAutoRefreshLoop();
        return ValueTask.CompletedTask;
    }

    internal static string Dash(object? value) =>
        value is null || value is "" ? "-" : Convert.ToString(value) ?? "-";

    internal static string FormatTime(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("g") : "-";

    internal static string LicenseLabel(string? value) => value switch
    {
        "company" => "회사",
        "byo" => "BYO",
        _ => "-"
    };

    internal static string ClassifyStorage(SoftwarePolicyClassification classification) => classification switch
    {
        SoftwarePolicyClassification.Whitelist => "white",
        SoftwarePolicyClassification.Managed => "managed",
        SoftwarePolicyClassification.Blacklist => "black",
        _ => "managed"
    };

    internal static bool TryParseClassification(string value, out SoftwarePolicyClassification classification) =>
        SoftwarePolicyClassificationNames.TryParse(value, out classification);

    internal static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
