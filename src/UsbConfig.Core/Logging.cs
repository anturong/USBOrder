namespace UsbDriveManager.Core;

/// <summary>最小日志接口：Core 只依赖它，服务端再叠加 EventLog。</summary>
public interface ILog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

public sealed class NullLog : ILog
{
    public static NullLog Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}

/// <summary>
/// 文件日志：按天分文件，进程内串行写入；失败不影响主流程。
/// 权威记录落在 ProgramData\UsbDriveManager\logs。
/// </summary>
public sealed class FileLogger : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _prefix;
    private bool _disposed;

    public FileLogger(string? directory = null, string prefix = "service")
    {
        _directory = PrepareDirectory(directory ?? AppPaths.LogDirectory);
        _prefix = prefix;
    }

    public string LogDirectory => _directory;

    /// <summary>首选目录不可写时依次退化到临时目录、程序目录；全都不可用则静默丢弃日志。</summary>
    private static string PrepareDirectory(string preferred)
    {
        var candidates = new[]
        {
            preferred,
            Path.Combine(Path.GetTempPath(), "UsbDriveManager", "logs"),
            Path.Combine(AppContext.BaseDirectory, "logs"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                System.IO.Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch
            {
                // 尝试下一个候选。
            }
        }

        return "";
    }

    public void Info(string message) => Write("INFO ", message, null);

    public void Warn(string message) => Write("WARN ", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        if (_disposed || _directory.Length == 0)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            try
            {
                var path = Path.Combine(_directory, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // 日志失败绝不反噬业务。
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
