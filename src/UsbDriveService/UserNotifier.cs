using System.Runtime.InteropServices;
using UsbDriveManager.Core;

namespace UsbDriveManager.Service;

/// <summary>
/// 向活动控制台会话弹系统消息框（WTSSendMessage）：
/// 专为 Session 0 服务设计，不需要托盘常驻程序；锁屏/无会话时只记日志。
/// 独立线程 + 30 秒超时，绝不阻塞 WMI 回调。
/// </summary>
internal static class UserNotifier
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopMost = 0x00040000;
    private const uint TimeoutSeconds = 30;
    private const int InvalidSession = -1;

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSSendMessage(
        IntPtr server,
        int sessionId,
        string title,
        int titleLength,
        string message,
        int messageLength,
        uint style,
        uint timeout,
        out uint response,
        bool wait);

    public static void Notify(string title, string message, ILog log)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == InvalidSession)
        {
            log.Warn($"没有活动控制台会话，跳过弹窗：{title} - {message}");
            return;
        }

        // 独立线程：WTSSendMessage 会等到用户点掉或超时。
        var thread = new Thread(() =>
        {
            try
            {
                var ok = WTSSendMessage(
                    IntPtr.Zero,
                    sessionId,
                    title,
                    title.Length * 2,
                    message,
                    message.Length * 2,
                    MbOk | MbIconWarning | MbTopMost | MbSetForeground,
                    TimeoutSeconds,
                    out _,
                    wait: true);

                if (!ok)
                {
                    log.Warn($"弹窗失败（会话 {sessionId}）：{Marshal.GetLastWin32Error()}");
                }
            }
            catch (Exception ex)
            {
                log.Warn($"弹窗异常：{ex.Message}");
            }
        })
        {
            IsBackground = true,
            Name = "UsbDriveService.Notify",
        };

        thread.Start();
    }
}
