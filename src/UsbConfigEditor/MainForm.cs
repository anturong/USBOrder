using System.Diagnostics;
using System.ServiceProcess;
using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Editor.Services;

namespace UsbDriveManager.Editor;

public sealed class MainForm : Form
{
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Button _installButton = new() { Text = "安装服务", AutoSize = true };
    private readonly Button _startButton = new() { Text = "启动", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "停止", AutoSize = true };
    private readonly Button _uninstallButton = new() { Text = "卸载服务", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
    private readonly Label _configSummary = new();
    private readonly ListView _rulesList = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };

    public MainForm()
    {
        Text = "USB 盘符管理器 — 配置编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(820, 560);
        Font = new Font("Microsoft YaHei UI", 9F);

        Controls.Add(BuildConfigBox());
        Controls.Add(BuildServiceBox());

        _timer.Tick += (_, _) => RefreshStatus();
        Load += (_, _) =>
        {
            RefreshStatus();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Stop();

        _installButton.Click += (_, _) => InstallService();
        _startButton.Click += (_, _) => RunBusy("启动服务", ServiceManager.Start);
        _stopButton.Click += (_, _) => RunBusy("停止服务", ServiceManager.Stop);
        _uninstallButton.Click += (_, _) => UninstallService();
        _refreshButton.Click += (_, _) => RefreshStatus();
    }

    private GroupBox BuildServiceBox()
    {
        var box = new GroupBox
        {
            Text = "服务状态",
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(12),
        };

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        _statusLabel.Location = new Point(14, 28);

        _detailLabel.AutoSize = true;
        _detailLabel.ForeColor = SystemColors.GrayText;
        _detailLabel.Location = new Point(14, 62);

        var buttons = new FlowLayoutPanel
        {
            Location = new Point(10, 90),
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };

        buttons.Controls.AddRange(new Control[]
        {
            _installButton, _startButton, _stopButton, _uninstallButton, _refreshButton,
        });

        box.Controls.Add(_statusLabel);
        box.Controls.Add(_detailLabel);
        box.Controls.Add(buttons);
        return box;
    }

    private GroupBox BuildConfigBox()
    {
        var box = new GroupBox
        {
            Text = "规则与配置",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _configSummary.AutoSize = true;
        _configSummary.Padding = new Padding(0, 0, 0, 8);
        layout.Controls.Add(_configSummary, 0, 0);

        _rulesList.Dock = DockStyle.Fill;
        _rulesList.View = View.Details;
        _rulesList.FullRowSelect = true;
        _rulesList.GridLines = true;
        _rulesList.Columns.Add("名称", 170);
        _rulesList.Columns.Add("VID", 60);
        _rulesList.Columns.Add("PID", 60);
        _rulesList.Columns.Add("序列号", 170);
        _rulesList.Columns.Add("目标盘符", 70);
        _rulesList.Columns.Add("启用", 50);
        layout.Controls.Add(_rulesList, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };

        var openData = new Button { Text = "打开数据目录", AutoSize = true };
        openData.Click += (_, _) => OpenFolder(AppPaths.DataDirectory);

        var openConfig = new Button { Text = "打开 config.json", AutoSize = true };
        openConfig.Click += (_, _) => OpenFile(AppPaths.ConfigFile);

        var openLogs = new Button { Text = "打开日志目录", AutoSize = true };
        openLogs.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);

        footer.Controls.AddRange(new Control[] { openData, openConfig, openLogs });
        layout.Controls.Add(footer, 0, 2);

        box.Controls.Add(layout);
        return box;
    }

    private void RefreshStatus()
    {
        var status = ServiceManager.GetStatus();

        if (status is null)
        {
            _statusLabel.Text = "● 未安装";
            _statusLabel.ForeColor = Color.Gray;
            _detailLabel.Text = "尚未注册服务。点“安装服务”创建专用账号并注册。";
        }
        else if (status == ServiceControllerStatus.Running)
        {
            _statusLabel.Text = "● 运行中";
            _statusLabel.ForeColor = Color.SeaGreen;
            _detailLabel.Text = $"程序：{ServiceManager.InstalledExePath}";
        }
        else
        {
            _statusLabel.Text = $"● {TranslateStatus(status.Value)}";
            _statusLabel.ForeColor = Color.DarkOrange;
            _detailLabel.Text = $"程序：{ServiceManager.InstalledExePath}";
        }

        _startButton.Enabled = status is not null && status != ServiceControllerStatus.Running;
        _stopButton.Enabled = status == ServiceControllerStatus.Running;
        _uninstallButton.Enabled = status is not null;

        RefreshConfig();
    }

    private void RefreshConfig()
    {
        try
        {
            var config = new ConfigStore().Load();
            _configSummary.Text = $"配置文件：{AppPaths.ConfigFile}    共 {config.Rules.Count} 条规则"
                                  + "    （规则编辑在下一版；当前可打开 config.json 手改后重启服务）";

            _rulesList.BeginUpdate();
            _rulesList.Items.Clear();

            foreach (var rule in config.Rules)
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(rule.Name) ? "(未命名)" : rule.Name);
                item.SubItems.Add(rule.Vid);
                item.SubItems.Add(rule.Pid);
                item.SubItems.Add(string.IsNullOrWhiteSpace(rule.Serial) ? "*" : rule.Serial);
                item.SubItems.Add($"{rule.TargetLetter}:");
                item.SubItems.Add(rule.Enabled ? "是" : "否");
                _rulesList.Items.Add(item);
            }

            _rulesList.EndUpdate();
        }
        catch (Exception ex)
        {
            _configSummary.Text = $"读取配置失败：{ex.Message}";
            _rulesList.Items.Clear();
        }
    }

    private void InstallService()
    {
        using var dialog = new InstallServiceDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunBusy("安装服务", () =>
        {
            ServiceManager.Stop();
            var executable = ServicePayload.PrepareInstallFiles();

            AccountManager.EnsureAccount(dialog.AccountName, dialog.Password);
            AccountManager.EnsureAdministrators(dialog.AccountName);
            AccountManager.GrantServiceLogonRight(dialog.AccountName);
            AccountManager.EnsureDataDirectoryAcl(dialog.AccountName);

            ServiceManager.Install(executable, dialog.AccountName, dialog.Password);
            ServiceManager.Start();
        });

        if (ServiceManager.GetStatus() == ServiceControllerStatus.Running)
        {
            MessageBox.Show(this, "服务已安装并启动。", "安装服务", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void UninstallService()
    {
        var answer = MessageBox.Show(
            this,
            "将停止并删除服务（配置与日志保留）。确定继续？",
            "卸载服务",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        RunBusy("卸载服务", ServiceManager.Uninstall);
    }

    private void RunBusy(string title, Action action)
    {
        try
        {
            UseWaitCursor = true;
            _installButton.Enabled = false;
            _startButton.Enabled = false;
            _stopButton.Enabled = false;
            _uninstallButton.Enabled = false;

            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"{title}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshStatus();
        }
    }

    private static string TranslateStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => "已停止",
        ServiceControllerStatus.StartPending => "正在启动",
        ServiceControllerStatus.StopPending => "正在停止",
        ServiceControllerStatus.Paused => "已暂停",
        _ => status.ToString(),
    };

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"文件不存在：{path}", "打开文件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开文件失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
