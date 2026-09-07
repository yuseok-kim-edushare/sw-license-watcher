using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal sealed class UninstallForm : Form
{
    private readonly CompanySettings _settings;
    private readonly Label _status;
    private readonly Button _start;
    private readonly Button _cancelWait;
    private CancellationTokenSource? _wait;

    public UninstallForm(CompanySettings settings, SetupArguments arguments)
    {
        _settings = settings;
        _ = arguments;

        Text = SetupPaths.ProductName + " 제거";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 220);
        Font = new Font("Segoe UI", 9F);

        var intro = new Label
        {
            AutoSize = false,
            Location = new Point(20, 16),
            Size = new Size(480, 56),
            Text = "제거하려면 /admin의 제거 요청 탭에서 관리자가 승인해야 합니다. 승인 전에는 서비스가 그대로 남습니다."
        };
        _status = new Label
        {
            AutoSize = false,
            Location = new Point(20, 84),
            Size = new Size(480, 56),
            ForeColor = Color.DimGray,
            Text = "시작하려면 제거 요청을 보내세요."
        };
        _start = new Button
        {
            Location = new Point(216, 164),
            Size = new Size(140, 32),
            Text = "제거 요청",
            UseVisualStyleBackColor = true
        };
        _start.Click += async (_, _) => await RequestUninstallAsync();
        _cancelWait = new Button
        {
            Location = new Point(364, 164),
            Size = new Size(136, 32),
            Text = "대기 취소",
            Enabled = false,
            UseVisualStyleBackColor = true
        };
        _cancelWait.Click += (_, _) => _wait?.Cancel();

        Controls.AddRange(intro, _status, _start, _cancelWait);
        AcceptButton = _start;
    }

    private async Task RequestUninstallAsync()
    {
        _start.Enabled = false;
        _cancelWait.Enabled = true;
        _wait = new CancellationTokenSource();
        var token = _wait.Token;
        try
        {
            var deviceCode = AgentSetupService.ReadInstalledDeviceCode(Environment.MachineName);
            using var http = UninstallApiClient.Create(_settings.ServerBaseUrl, _settings.AgentToken);
            var client = new UninstallApiClient(http);
            _status.Text = "제거 요청을 보내는 중입니다...";
            var created = await client.CreateAsync(deviceCode, token);
            _status.Text = $"관리자 승인 대기 중 (요청 {created.Id}, {deviceCode})...";

            var deadline = DateTime.UtcNow.AddHours(2);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var status = await client.GetAsync(created.Id, deviceCode, token);
                if (string.Equals(status.Status, "approved", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(status.Code))
                    {
                        throw new InvalidOperationException("승인은 되었지만 해제 코드가 없습니다.");
                    }

                    await client.ConsumeAsync(created.Id, deviceCode, status.Code, token);
                    AgentSetupService.RemoveInstalledFiles(removeState: false);
                    _status.ForeColor = Color.DarkGreen;
                    _status.Text = "제거가 끝났습니다.";
                    MessageBox.Show(
                        "서비스와 설치 파일을 제거했습니다.",
                        SetupPaths.ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    Close();
                    return;
                }

                if (string.Equals(status.Status, "denied", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("제거 요청이 거절되었습니다. 서비스는 그대로입니다.");
                }

                if (string.Equals(status.Status, "expired", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.Status, "consumed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"제거 권한이 {status.Status} 상태입니다. 서비스는 그대로입니다.");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }

            throw new TimeoutException("관리자 승인 대기 시간이 지났습니다. 서비스는 그대로입니다.");
        }
        catch (OperationCanceledException)
        {
            _status.Text = "대기를 취소했습니다. 서비스는 그대로입니다.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
        }
        finally
        {
            _start.Enabled = true;
            _cancelWait.Enabled = false;
            _wait?.Dispose();
            _wait = null;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _wait?.Cancel();
        _wait?.Dispose();
        base.OnFormClosed(e);
    }
}
