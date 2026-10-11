using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.Relational;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSwLicenseWatcherInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SqlServerStorageOptions>>().Value);
        services.AddSingleton<IRelationalDialect>(sp =>
            RelationalDialects.Create(sp.GetRequiredService<IOptions<StorageProviderOptions>>().Value.Provider));
        services.AddSingleton<ISchemaScriptBuilder>(sp =>
            SchemaScriptBuilders.Create(StorageProviders.Parse(
                sp.GetRequiredService<IOptions<StorageProviderOptions>>().Value.Provider)));
        services.AddSingleton<RelationalSchemaApplicator>();
        services.AddSingleton(sp => new RelationalDataContext(
            sp.GetRequiredService<SqlServerStorageOptions>(),
            sp.GetRequiredService<IRelationalDialect>()));

        services.AddSingleton<SqlServerHealthRepository>();
        services.AddSingleton<IHealthProbe>(sp => sp.GetRequiredService<SqlServerHealthRepository>());
        services.AddSingleton<SqlServerSnapshotRepository>();
        services.AddSingleton<ISnapshotRepository>(sp => sp.GetRequiredService<SqlServerSnapshotRepository>());
        services.AddSingleton<SqlServerHeartbeatRepository>();
        services.AddSingleton<IHeartbeatRepository>(sp => sp.GetRequiredService<SqlServerHeartbeatRepository>());
        services.AddSingleton<SqlServerDeviceQueryRepository>();
        services.AddSingleton<IDeviceQuery>(sp => sp.GetRequiredService<SqlServerDeviceQueryRepository>());
        services.AddSingleton<SqlServerSoftwareQueryRepository>();
        services.AddSingleton<ISoftwareQuery>(sp => sp.GetRequiredService<SqlServerSoftwareQueryRepository>());
        services.AddSingleton<SqlServerPolicyRepository>();
        services.AddSingleton<IPolicyStore>(sp => sp.GetRequiredService<SqlServerPolicyRepository>());
        services.AddSingleton<SqlServerViolationQueryRepository>();
        services.AddSingleton<IViolationQuery>(sp => sp.GetRequiredService<SqlServerViolationQueryRepository>());
        services.AddSingleton<SqlServerUninstallRepository>();
        services.AddSingleton<IUninstallRequestStore>(sp => sp.GetRequiredService<SqlServerUninstallRepository>());
        services.AddSingleton<SqlServerUserMessageRepository>();
        services.AddSingleton<IUserMessageStore>(sp => sp.GetRequiredService<SqlServerUserMessageRepository>());
        services.AddSingleton<SqlServerWorkerUpdatePinRepository>();
        services.AddSingleton<IWorkerUpdatePinStore>(sp => sp.GetRequiredService<SqlServerWorkerUpdatePinRepository>());

        return services;
    }
}
