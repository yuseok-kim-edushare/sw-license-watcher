using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal sealed class InstallForm : Form
{
    private readonly CompanySettings _settings;
    private readonly AgentSetupOrchestrator _setup;
    private readonly ExistingAgentInstallation _existing;
    private readonly InPlaceUpgradeAuthorizer? _upgradeAuthorizer;
    private readonly string? _sourceExePath;
    private readonly string _payloadDirectory;
    private readonly TextBox _assetCode;
    private readonly Label _preview;
    private readonly Label _status;
    private readonly Button _install;

    public InstallForm(
        CompanySettings settings,
        string payloadDirectory,
        string? sourceExePath,
        AgentSetupOrchestrator setup,
        InPlaceUpgradeAuthorizer? upgradeAuthorizer = null)
    {
        _settings = settings;
        _payloadDirectory = payloadDirectory;
        _sourceExePath = sourceExePath;
        _setup = setup;
        _upgradeAuthorizer = upgradeAuthorizer;
        _existing = setup.DetectExistingInstallation();

        Text = SetupPaths.ProductName + (_existing.IsPresent ? " 업그레이드" : " 설치");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, _existing.IsPresent ? 316 : 280);
        Font = new Font("Segoe UI", 9F);

        var intro = new Label
        {
            AutoSize = false,
            Location = new Point(20, 16),
            Size = new Size(480, _existing.IsPresent ? 72 : 40),
            Text = UpgradeIntroText(_existing)
        };
        var contentTop = _existing.IsPresent ? 96 : 64;
        var server = new Label
        {
            AutoSize = false,
            Location = new Point(20, contentTop),
            Size = new Size(480, 20),
            Text = "서버: " + settings.ServerBaseUrl
        };
        var assetLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, contentTop + 32),
            Text = "PC 관리 식별자 / 자산번호 (선택)"
        };
        _assetCode = new TextBox
        {
            Location = new Point(20, contentTop + 54),
            Size = new Size(480, 27),
            Text = _existing.PreferredDeviceCode ?? string.Empty
        };
        _assetCode.TextChanged += (_, _) => UpdatePreview();
        _preview = new Label
        {
            AutoSize = false,
            Location = new Point(20, contentTop + 90),
            Size = new Size(480, 20)
        };
        _status = new Label
        {
            AutoSize = false,
            Location = new Point(20, contentTop + 122),
            Size = new Size(480, 36),
            ForeColor = Color.DimGray,
            Text = UpgradeStatusText(_existing)
        };
        _install = new Button
        {
            Location = new Point(324, contentTop + 168),
            Size = new Size(176, 32),
            Text = _existing.IsPresent ? "업그레이드" : "설치",
            UseVisualStyleBackColor = true
        };
        _install.Click += async (_, _) => await InstallAsync();

        Controls.AddRange(intro, server, assetLabel, _assetCode, _preview, _status, _install);
        AcceptButton = _install;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (DeviceCodeResolver.TryResolveForInstall(
                _assetCode.Text,
                Environment.MachineName,
                _existing.PreferredDeviceCode,
                out var deviceCode,
                out _))
        {
            _preview.Text = PreviewText(deviceCode);
            _install.Enabled = true;
        }
        else
        {
            _preview.Text = "자산번호가 너무 깁니다.";
            _install.Enabled = false;
        }
    }

    private string PreviewText(string deviceCode)
    {
        var assigned = _existing.AssignedDeviceCode;
        if (!string.IsNullOrWhiteSpace(assigned) &&
            !string.Equals(assigned, deviceCode, StringComparison.Ordinal))
        {
            return "등록 이름: " + deviceCode + " (서버 지정: " + assigned + ")";
        }

        return "등록 이름: " + deviceCode;
    }

    private static string UpgradeIntroText(ExistingAgentInstallation existing)
    {
        if (!existing.IsPresent)
        {
            return "서버 주소와 에이전트 키는 설치본에 들어 있습니다. 자산번호를 모르면 비워 두세요.";
        }

        if (existing.RequiresServerKeyAuthorization)
        {
            return "이미 설치된 서비스를 발견했습니다. 서버에서 장치 인증서를 가져와 확인한 뒤 인플레이스 업그레이드합니다. 제거 요청은 남기지 않습니다.";
        }

        return "이미 설치된 서비스를 발견했습니다. 0.1.0 이전이므로 비대칭키 확인을 건너뛰고 인플레이스 업그레이드합니다. 제거 요청은 남기지 않습니다.";
    }

    private static string UpgradeStatusText(ExistingAgentInstallation existing)
    {
        if (!existing.IsPresent)
        {
            return string.Empty;
        }

        return existing.RequiresServerKeyAuthorization
            ? "삭제 허가: 서버 장치 키 확인 후 우회"
            : "삭제 허가: 건너뜀 (0.1.0 이전 · 비대칭키 확인 없음)";
    }

    private async Task InstallAsync()
    {
        if (!DeviceCodeResolver.TryResolveForInstall(
                _assetCode.Text,
                Environment.MachineName,
                _existing.PreferredDeviceCode,
                out var deviceCode,
                out var error))
        {
            _status.Text = error;
            return;
        }

        _install.Enabled = false;
        _status.Text = _existing.IsPresent
            ? (_existing.RequiresServerKeyAuthorization
                ? "서버에서 장치 키를 확인하는 중입니다..."
                : "인플레이스 업그레이드 중입니다...")
            : "설치하는 중입니다...";
        UseWaitCursor = true;
        try
        {
            if (_existing.IsPresent)
            {
                if (_upgradeAuthorizer is null)
                {
                    throw new InvalidOperationException("업그레이드 확인을 준비하지 못했습니다.");
                }

                await _upgradeAuthorizer.AuthorizeAsync(_existing, deviceCode, CancellationToken.None);
                _status.Text = "인플레이스 업그레이드 중입니다...";
            }

            _setup.Install(_settings, _payloadDirectory, deviceCode, _sourceExePath);
            _status.ForeColor = Color.DarkGreen;
            _status.Text = _existing.IsPresent
                ? "업그레이드가 끝났습니다. 서비스가 실행 중입니다."
                : "설치가 끝났습니다. 서비스가 실행 중입니다.";
            MessageBox.Show(
                (_existing.IsPresent ? "업그레이드가 끝났습니다." : "설치가 끝났습니다.") +
                "\n등록 이름: " + deviceCode,
                SetupPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
            _install.Enabled = true;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
