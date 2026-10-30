using System.Runtime.InteropServices;

namespace UsbDriveManager.Editor;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            // WinExe 本身没有控制台：挂到父控制台，终端里能看到结果；
            // 同时自检会写一份 %TEMP%\usbdrive-editor-selftest-*.log，重定向场景也能取到。
            AttachConsole(-1);
            return SelfTest.Run();
        }

        ApplicationConfiguration.Initialize();

        // --profile <name>：用独立的 WebView2 用户数据目录，便于多开调试/截图。
        var profileIndex = Array.FindIndex(args, arg => arg.Equals("--profile", StringComparison.OrdinalIgnoreCase));
        var profile = profileIndex >= 0 && profileIndex + 1 < args.Length ? args[profileIndex + 1] : "";

        // WebView2 承载 React 界面；旧的 WinForms 版 MainForm 保留作参考，不再启动。
        Application.Run(new WebViewForm(profile));
        return 0;
    }
}
