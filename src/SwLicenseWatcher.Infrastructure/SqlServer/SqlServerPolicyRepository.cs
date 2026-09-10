using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerPolicyRepository(SqlServerDataContext context) : IPolicyStore
{
    public Task<(int TotalCount, List<SoftwarePolicyEntry> Items)> ListPoliciesAsync(
        int skip, int take, string? search, string? classification, CancellationToken cancellationToken) =>
        context.ListPoliciesAsync(skip, take, search, classification, cancellationToken);

    public Task<SoftwarePolicyEntry?> GetPolicyAsync(long id, CancellationToken cancellationToken) =>
        context.GetPolicyAsync(id, cancellationToken);

    public Task<SoftwarePolicyEntry> CreatePolicyAsync(
        SoftwarePolicyWriteRequest request, CancellationToken cancellationToken) =>
        context.CreatePolicyAsync(request, cancellationToken);

    public Task<SoftwarePolicyEntry?> UpdatePolicyAsync(
        long id, SoftwarePolicyWriteRequest request, CancellationToken cancellationToken) =>
        context.UpdatePolicyAsync(id, request, cancellationToken);

    public Task<bool> DeletePolicyAsync(long id, CancellationToken cancellationToken) =>
        context.DeletePolicyAsync(id, cancellationToken);

    public Task<SoftwarePolicyEntry> UpsertSoftwareClassificationAsync(
        string productName, SoftwareClassificationWriteRequest request, CancellationToken cancellationToken) =>
        context.UpsertSoftwareClassificationAsync(productName, request, cancellationToken);

    public Task<IReadOnlyList<SoftwarePolicyEntry>> UpsertSoftwareClassificationsAsync(
        IReadOnlyList<(string Name, SoftwareClassificationWriteRequest Request)> items,
        CancellationToken cancellationToken) =>
        context.UpsertSoftwareClassificationsAsync(items, cancellationToken);

    public Task<bool> SetDeviceSoftwareLicenseSourceAsync(
        string deviceCode, string softwareName, string? licenseSource, CancellationToken cancellationToken) =>
        context.SetDeviceSoftwareLicenseSourceAsync(deviceCode, softwareName, licenseSource, cancellationToken);

}
