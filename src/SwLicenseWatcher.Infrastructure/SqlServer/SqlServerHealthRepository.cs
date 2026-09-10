using SwLicenseWatcher.Application;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerHealthRepository(SqlServerDataContext context) : IHealthProbe
{
    public Task ProbeAsync(CancellationToken cancellationToken) =>
        context.ProbeAsync(cancellationToken);
}
