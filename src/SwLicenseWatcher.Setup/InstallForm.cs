using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal sealed class InstallForm : Form
{
    private readonly CompanySettings _settings;
    private readonly SetupArguments _arguments;
    private readonly string _payloadDirectory;
    private readonly TextBox _assetCode;
    private readonly Label _preview;
    private readonly Label _status;
    private readonly Button _install;

    public InstallForm(CompanySettings settings, SetupArguments arguments, string payloadDirectory)
    {
        _settings = settings;
        _arguments = arguments;
        _payloadDirectory = payloadDirectory;

        Text = SetupPaths.ProductName + " 설치";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 280);
        Font = new Font("Segoe UI", 9F);

        var intro = new Label
        {
            AutoSize = false,
            Location = new Point(20, 16),
            Size = new Size(480, 40),
            Text = "서버 주소와 에이전트 키는 설치본에 들어 있습니다. 자산번호를 모르면 비워 두세요."
        };
        var server = new Label
        {
            AutoSize = false,
            Location = new Point(20, 64),
            Size = new Size(480, 20),
            Text = "서버: " + settings.ServerBaseUrl
        };
        var assetLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 96),
            Text = "PC 관리 식별자 / 자산번호 (선택)"
        };
        _assetCode = new TextBox
        {
            Location = new Point(20, 118),
            Size = new Size(480, 27)
        };
        _assetCode.TextChanged += (_, _) => UpdatePreview();
        _preview = new Label
        {
            AutoSize = false,
            Location = new Point(20, 154),
            Size = new Size(480, 20)
        };
        _status = new Label
        {
            AutoSize = false,
            Location = new Point(20, 186),
            Size = new Size(480, 36),
            ForeColor = Color.DimGray
        };
        _install = new Button
        {
            Location = new Point(324, 232),
            Size = new Size(176, 32),
            Text = "설치",
            UseVisualStyleBackColor = true
        };
        _install.Click += (_, _) => Install();

        Controls.AddRange(intro, server, assetLabel, _assetCode, _preview, _status, _install);
        AcceptButton = _install;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (DeviceCodeResolver.TryResolve(_assetCode.Text, Environment.MachineName, out var deviceCode, out _))
        {
            _preview.Text = "등록 이름: " + deviceCode;
            _install.Enabled = true;
        }
        else
        {
            _preview.Text = "자산번호가 너무 깁니다.";
            _install.Enabled = false;
        }
    }

    private void Install()
    {
        if (!DeviceCodeResolver.TryResolve(_assetCode.Text, Environment.MachineName, out var deviceCode, out var error))
        {
            _status.Text = error;
            return;
        }

        _install.Enabled = false;
        _status.Text = "설치하는 중입니다...";
        UseWaitCursor = true;
        try
        {
            AgentSetupService.Install(_settings, _payloadDirectory, deviceCode, _arguments.SourceExePath);
            _status.ForeColor = Color.DarkGreen;
            _status.Text = "설치가 끝났습니다. 서비스가 실행 중입니다.";
            MessageBox.Show(
                "설치가 끝났습니다.\n등록 이름: " + deviceCode,
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
