using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerViolationQueryRepository(SqlServerDataContext context) : IViolationQuery
{
    public Task<(int TotalCount, List<SoftwareViolationEntry> Items)> ListViolationsAsync(
        int skip,
        int take,
        string? search,
        DateTimeOffset? since,
        CancellationToken cancellationToken) =>
        context.ListViolationsAsync(skip, take, search, since, cancellationToken);
}
