using System.ServiceProcess;

namespace UsbDriveManager.Service;

public sealed class UsbDriveWindowsService : ServiceBase
{
    private ServiceHost? _host;

    public UsbDriveWindowsService()
    {
        ServiceName = "UsbDriveService";
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        // OnStart 只做订阅与建档，耗时操作全部丢到后台线程，避免 SCM 的 1053 超时。
        _host = new ServiceHost(console: false);
        _host.Start();
    }

    protected override void OnStop()
    {
        _host?.Dispose();
        _host = null;
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }
}
