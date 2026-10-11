using SwLicenseWatcher.Application;
using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerHealthRepository(RelationalDataContext context) : IHealthProbe
{
    public Task ProbeAsync(CancellationToken cancellationToken) =>
        context.ProbeAsync(cancellationToken);
}
