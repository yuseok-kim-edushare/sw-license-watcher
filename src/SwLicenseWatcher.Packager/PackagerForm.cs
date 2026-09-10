using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Packager;

internal sealed class PackagerForm : Form
{
    private readonly PackagingWorkflow _workflow;
    private readonly TextBox _serverUrl;
    private readonly TextBox _agentToken;
    private readonly TextBox _releaseZip;
    private readonly TextBox _output;
    private readonly Label _status;
    private readonly Button _build;

    public PackagerForm(PackagingWorkflow workflow)
    {
        _workflow = workflow;
        Text = "SW License Watcher 패키저";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 360);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(LabelAt(20, 16, "서버 주소 (필수)"));
        _serverUrl = TextAt(20, 36, 600);
        _serverUrl.PlaceholderText = "https://license-watcher.example.local";
        Controls.Add(_serverUrl);

        Controls.Add(LabelAt(20, 72, "에이전트 키 (필수, 32자 이상)"));
        _agentToken = TextAt(20, 92, 600);
        _agentToken.UseSystemPasswordChar = true;
        Controls.Add(_agentToken);

        Controls.Add(LabelAt(20, 128, "공식 Release ZIP (패키저 옆에 에이전트가 있으면 비워도 됩니다)"));
        _releaseZip = TextAt(20, 148, 500);
        Controls.Add(_releaseZip);
        var browseZip = new Button
        {
            Location = new Point(528, 146),
            Size = new Size(92, 28),
            Text = "찾기",
            UseVisualStyleBackColor = true
        };
        browseZip.Click += (_, _) => BrowseZip();
        Controls.Add(browseZip);

        Controls.Add(LabelAt(20, 184, "회사 Setup.exe 저장 위치"));
        _output = TextAt(20, 204, 500);
        _output.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            PayloadLayout.LauncherFileName);
        Controls.Add(_output);
        var browseOut = new Button
        {
            Location = new Point(528, 202),
            Size = new Size(92, 28),
            Text = "저장",
            UseVisualStyleBackColor = true
        };
        browseOut.Click += (_, _) => BrowseOutput();
        Controls.Add(browseOut);

        _status = new Label
        {
            AutoSize = false,
            Location = new Point(20, 248),
            Size = new Size(600, 40),
            ForeColor = Color.DimGray,
            Text = "서버 주소와 키를 넣고 회사 설치본을 만듭니다. SDK는 필요 없습니다."
        };
        Controls.Add(_status);

        _build = new Button
        {
            Location = new Point(400, 304),
            Size = new Size(220, 36),
            Text = "회사 설치본 만들기",
            UseVisualStyleBackColor = true
        };
        _build.Click += (_, _) => Build();
        Controls.Add(_build);
        AcceptButton = _build;
    }

    private void BrowseZip()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Release ZIP (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "SwLicenseWatcher 릴리스 ZIP"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _releaseZip.Text = dialog.FileName;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Setup (*.exe)|*.exe",
            FileName = PayloadLayout.LauncherFileName,
            Title = "회사 Setup.exe"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _output.Text = dialog.FileName;
        }
    }

    private void Build()
    {
        _build.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var searchRoot = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            _workflow.Build(new PackagingRequest(
                _serverUrl.Text,
                _agentToken.Text,
                _releaseZip.Text,
                _output.Text,
                searchRoot,
                AppContext.BaseDirectory));

            _status.ForeColor = Color.DarkGreen;
            _status.Text = "만들었습니다: " + _output.Text.Trim();
            MessageBox.Show(
                "회사 설치본을 만들었습니다.\n이 파일 하나만 직원에게 배포하세요.",
                "패키저",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
        }
        finally
        {
            _build.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static Label LabelAt(int x, int y, string text) => new()
    {
        AutoSize = true,
        Location = new Point(x, y),
        Text = text
    };

    private static TextBox TextAt(int x, int y, int width) => new()
    {
        Location = new Point(x, y),
        Size = new Size(width, 27)
    };
}
