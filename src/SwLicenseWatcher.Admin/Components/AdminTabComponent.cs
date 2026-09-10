using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin.Components;

public interface IAdminTab
{
    Task RefreshAsync();
}

public abstract class AdminTabComponent : ComponentBase, IAdminTab
{
    protected const int PageSize = 50;

    [Inject] protected AdminApiClient Api { get; set; } = default!;
    [Inject] protected AdminRequestHandler Requests { get; set; } = default!;
    [Parameter] public EventCallback Refreshed { get; set; }

    protected int Skip { get; set; }
    protected int TotalCount { get; set; }
    protected string Search { get; set; } = "";
    protected string Error { get; set; } = "";
    protected string Meta { get; set; } = "";

    protected int PageFrom => TotalCount == 0 ? 0 : Skip + 1;
    protected int PageTo => Math.Min(Skip + PageSize, TotalCount);
    protected bool CanPrev => Skip > 0;
    protected bool CanNext => Skip + PageSize < TotalCount;

    protected override Task OnInitializedAsync() => RefreshAsync();

    public abstract Task RefreshAsync();

    protected async Task<bool> LoadAsync(Func<Task> load)
    {
        Error = "";
        Meta = "불러오는 중...";
        var succeeded = await Requests.RunAsync(load, message =>
        {
            Error = message;
            Meta = "";
        });
        if (succeeded)
        {
            Meta = $"총 {TotalCount}건";
            await Refreshed.InvokeAsync();
        }

        return succeeded;
    }

    protected async Task QueryAsync()
    {
        Skip = 0;
        await RefreshAsync();
    }

    protected async Task PrevPageAsync()
    {
        Skip = Math.Max(0, Skip - PageSize);
        await RefreshAsync();
    }

    protected async Task NextPageAsync()
    {
        Skip += PageSize;
        await RefreshAsync();
    }

    protected async Task OnSearchKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await QueryAsync();
        }
    }

    protected string PagingQuery()
    {
        var parts = new List<string> { $"skip={Skip}", $"take={PageSize}" };
        AddSearch(parts);
        AddFilters(parts);
        return string.Join("&", parts);
    }

    protected string ExportQuery()
    {
        var parts = new List<string>();
        AddSearch(parts);
        AddFilters(parts);
        return string.Join("&", parts);
    }

    protected virtual void AddFilters(List<string> parts)
    {
    }

    private void AddSearch(List<string> parts)
    {
        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add("search=" + Uri.EscapeDataString(Search.Trim()));
        }
    }

    protected async Task DownloadCsvAsync(string path, string fileName)
    {
        Error = "";
        await Requests.RunAsync(async () =>
        {
            var query = ExportQuery();
            query += (query.Length == 0 ? "" : "&") + "format=csv";
            await Api.DownloadCsvAsync($"{path}?{query}", fileName);
        }, message => Error = message, "CSV를 받지 못했습니다.");
    }
}
