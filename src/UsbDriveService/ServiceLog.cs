using System.Diagnostics;
using UsbDriveManager.Core;

namespace UsbDriveManager.Service;

/// <summary>日志三写：文件（权威）、事件日志（运维可见）、控制台（--console 调试）。</summary>
internal sealed class ServiceLog : ILog, IDisposable
{
    private const string EventSource = "UsbDriveService";

    private readonly ILog _file;
    private readonly bool _console;
    private readonly EventLog? _eventLog;

    public ServiceLog(ILog file, bool console)
    {
        _file = file;
        _console = console;

        if (console)
        {
            return;
        }

        try
        {
            if (!EventLog.SourceExists(EventSource))
            {
                EventLog.CreateEventSource(EventSource, "Application");
            }

            _eventLog = new EventLog("Application") { Source = EventSource };
        }
        catch
        {
            // 没有管理员权限时创建源会失败；文件日志仍然可用。
            _eventLog = null;
        }
    }

    public void Info(string message) => Write("INFO ", message, EventLogEntryType.Information);

    public void Warn(string message) => Write("WARN ", message, EventLogEntryType.Warning);

    public void Error(string message, Exception? exception = null)
        => Write("ERROR", message + (exception is null ? "" : Environment.NewLine + exception), EventLogEntryType.Error);

    private void Write(string level, string message, EventLogEntryType type)
    {
        switch (level.Trim())
        {
            case "ERROR":
                _file.Error(message);
                break;
            case "WARN":
                _file.Warn(message);
                break;
            default:
                _file.Info(message);
                break;
        }

        if (_console)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{level}] {message}");
        }

        if (_eventLog is null)
        {
            return;
        }

        try
        {
            _eventLog.WriteEntry(message, type);
        }
        catch
        {
            // 事件日志写失败不影响业务。
        }
    }

    public void Dispose() => _eventLog?.Dispose();
}
