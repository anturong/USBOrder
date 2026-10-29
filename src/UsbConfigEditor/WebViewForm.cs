using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace UsbDriveManager.Editor;

/// <summary>
/// 用 WebView2 承载 React UI（NoteGen 风格的界面），后端仍是本地 C#。
/// 前端资源从程序目录的 wwwroot 通过虚拟域名加载，不占端口、不落临时文件。
/// </summary>
public sealed class WebViewForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    public WebViewForm()
    {
        Text = "USB 盘符管理器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1240, 800);
        BackColor = Color.FromArgb(0xFA, 0xFA, 0xFA);
        Icon = LoadAppIcon();

        Controls.Add(_webView);
        Load += async (_, _) => await InitializeAsync();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable))
            {
                return Icon.ExtractAssociatedIcon(executable);
            }
        }
        catch
        {
            // 取不到就用默认图标，不影响启动。
        }

        return null;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsbDriveManager",
                "EditorWebView");

            Directory.CreateDirectory(userData);

            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            core.SetVirtualHostNameToFolderMapping(
                "app.usbdrive",
                webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            core.Navigate("https://app.usbdrive/index.html");

            new EditorBridge(_webView).Attach();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"初始化 WebView2 失败：{ex.Message}\n\n确认系统已安装 WebView2 运行时（Win10/11 的 Edge 一般自带）。",
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }
}
