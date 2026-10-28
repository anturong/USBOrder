namespace UsbDriveManager.Editor;

/// <summary>安装服务前收集专用账号与密码。</summary>
internal sealed class InstallServiceDialog : Form
{
    private readonly TextBox _accountBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly TextBox _confirmBox = new();

    public InstallServiceDialog()
    {
        Text = "安装服务";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 320);
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "服务账号：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _accountBox.Text = "UsbDriveMgrSvc";
        _accountBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_accountBox, 1, 0);

        layout.Controls.Add(new Label { Text = "密码：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_passwordBox, 1, 1);

        layout.Controls.Add(new Label { Text = "确认密码：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _confirmBox.UseSystemPasswordChar = true;
        _confirmBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_confirmBox, 1, 2);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "将创建（或重置）本机账号并加入 Administrators、授予“作为服务登录”，"
                   + "密码设为永不过期。之后服务以该账号运行。",
            ForeColor = SystemColors.GrayText,
        };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };

        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "安装", AutoSize = true };
        ok.Click += (_, _) => Confirm();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string AccountName { get; private set; } = "";

    public string Password { get; private set; } = "";

    private void Confirm()
    {
        var account = _accountBox.Text.Trim();
        if (account.Length == 0)
        {
            MessageBox.Show(this, "账号不能为空。", "安装服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (account is "Administrator" or "SYSTEM" or "LocalSystem")
        {
            MessageBox.Show(this, "请使用专用账号，不要用内置管理员或系统账号。", "安装服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_passwordBox.Text.Length < 12)
        {
            MessageBox.Show(this, "密码至少 12 位。", "安装服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_passwordBox.Text != _confirmBox.Text)
        {
            MessageBox.Show(this, "两次输入的密码不一致。", "安装服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AccountName = account;
        Password = _passwordBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
