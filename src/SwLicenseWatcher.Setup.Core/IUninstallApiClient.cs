namespace SwLicenseWatcher.Setup.Core;

public interface IUninstallApiClient
{
    Task<UninstallRequestCreated> CreateAsync(string deviceCode, CancellationToken cancellationToken);

    Task<AgentUninstallRequest> GetAsync(
        long requestId,
        string deviceCode,
        CancellationToken cancellationToken);

    Task ConsumeAsync(
        long requestId,
        string deviceCode,
        string code,
        CancellationToken cancellationToken);
}
