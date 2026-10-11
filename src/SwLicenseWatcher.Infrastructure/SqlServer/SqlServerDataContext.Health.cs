using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, "SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
    }

}
