namespace SwLicenseWatcher.Setup.Core;

public sealed record PackagingRequest(
    string ServerBaseUrl,
    string AgentToken,
    string? ReleaseZipPath,
    string OutputExePath,
    string SearchRoot,
    string BaseDirectory);

public sealed class PackagingWorkflow
{
    private readonly ICompanyPackager _packager;

    public PackagingWorkflow(ICompanyPackager packager)
    {
        _packager = packager;
    }

    public void Build(PackagingRequest request)
    {
        PackagerPaths.TryDiscover(request.SearchRoot, out var bundle, out _);
        PackagerPaths.TryDiscover(request.BaseDirectory, out var baseBundle, out _);

        var launcher = bundle.LauncherStubPath ?? baseBundle.LauncherStubPath;
        var setupUi = bundle.SetupUiPath ?? baseBundle.SetupUiPath;
        var worker = bundle.WorkerDirectory ?? baseBundle.WorkerDirectory;
        var watchdog = bundle.WatchdogDirectory ?? baseBundle.WatchdogDirectory;
        string? extractedAgents = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(request.ReleaseZipPath))
            {
                extractedAgents = Path.Combine(
                    Path.GetTempPath(),
                    "SwLicenseWatcher-packager",
                    Guid.NewGuid().ToString("N"));
                ReleaseLayout.ExtractAgentsFromReleaseZip(request.ReleaseZipPath.Trim(), extractedAgents);
                ReleaseLayout.TryFindAgents(extractedAgents, out var extractedWorker, out var extractedWatchdog);
                worker = extractedWorker;
                watchdog = extractedWatchdog;
            }

            if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))
            {
                throw new InvalidOperationException(
                    "런처 뼈대(SwLicenseWatcher-Setup.exe)를 패키저 옆에서 찾지 못했습니다.");
            }

            if (AttachedPayload.HasPayload(launcher))
            {
                throw new InvalidOperationException(
                    "선택한 Setup.exe에 이미 페이로드가 붙어 있습니다. 릴리스의 빈 런처 스텁을 쓰세요.");
            }

            if (string.IsNullOrWhiteSpace(setupUi) || !File.Exists(setupUi))
            {
                throw new InvalidOperationException("setup-ui/SwLicenseWatcher.Setup.exe 를 찾지 못했습니다.");
            }

            if (string.IsNullOrWhiteSpace(worker) || string.IsNullOrWhiteSpace(watchdog))
            {
                throw new InvalidOperationException("에이전트 폴더 또는 공식 Release ZIP이 필요합니다.");
            }

            _packager.Build(
                request.ServerBaseUrl,
                request.AgentToken,
                launcher,
                setupUi,
                worker,
                watchdog,
                request.OutputExePath.Trim());
        }
        finally
        {
            TryDeleteDirectory(extractedAgents);
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
