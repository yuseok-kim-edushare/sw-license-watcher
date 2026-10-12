using SwLicenseWatcher.Application;
using SwLicenseWatcher.Infrastructure.Relational;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerViolationQueryRepository(RelationalDataContext context) : IViolationQuery
{
    public Task<(int TotalCount, List<SoftwareViolationEntry> Items)> ListViolationsAsync(
        int skip,
        int take,
        string? search,
        DateTimeOffset? since,
        CancellationToken cancellationToken,
        string? sort = null,
        bool sortDescending = false) =>
        context.ListViolationsAsync(skip, take, search, since, cancellationToken, sort, sortDescending);
}
