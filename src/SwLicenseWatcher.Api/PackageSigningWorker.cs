using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal interface IPackageFileSigner
{
    Task SignBinariesAsync(string directory, CancellationToken cancellationToken);
}

internal sealed class PackageSigningWorker(
    IOptions<ArtifactSigningOptions> options,
    IPackageFileSigner signer,
    ILogger<PackageSigningWorker> logger) : BackgroundService, IUpdatePackageSigner
{
    private readonly Channel<SignWork> _channel = Channel.CreateUnbounded<SignWork>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public bool SignClientPackages => options.Value.SignClientPackages;

    public bool SignServerPackages => options.Value.SignServerPackages;

    public bool ShouldSignClient => options.Value.ShouldSignClient;

    public bool ShouldSignServer => options.Value.ShouldSignServer;

    public async Task SignBinariesAsync(string directory, CancellationToken cancellationToken)
    {
        var work = new SignWork(directory, cancellationToken);
        await _channel.Writer.WriteAsync(work, cancellationToken);
        await work.Completion.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await signer.SignBinariesAsync(work.Directory, work.Cancellation);
                work.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background package signing failed.");
                work.Completion.TrySetException(ex);
            }
        }
    }

    private sealed class SignWork(string directory, CancellationToken cancellation)
    {
        public string Directory { get; } = directory;

        public CancellationToken Cancellation { get; } = cancellation;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
