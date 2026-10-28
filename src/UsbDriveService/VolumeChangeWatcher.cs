using System.Management;

namespace UsbDriveManager.Service;

public sealed record VolumeChangeEvent(int EventType, string DriveName)
{
    public const int Arrival = 2;

    public const int Removal = 3;
}

/// <summary>
/// 订阅 Win32_VolumeChangeEvent：插入（2）与拔出（3）都收。
/// 回调里只做分发，实际处理放到线程池并延迟 500ms（等卷就绪）。
/// </summary>
public sealed class VolumeChangeWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;

    public event Action<VolumeChangeEvent>? VolumeChanged;

    public bool TryStart(out string? error)
    {
        try
        {
            var scope = new ManagementScope(@"root\CIMV2");
            scope.Connect();

            var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
            _watcher = new ManagementEventWatcher(scope, query);
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var eventType = Convert.ToInt32(e.NewEvent["EventType"] ?? 0);
            var driveName = e.NewEvent["DriveName"]?.ToString() ?? "";

            VolumeChanged?.Invoke(new VolumeChangeEvent(eventType, driveName));
        }
        catch
        {
            // 单次事件解析失败不能拖垮订阅线程。
        }
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.Stop();
        }
        catch
        {
            // 停止失败时交给 Dispose。
        }

        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
        _watcher = null;
    }
}
