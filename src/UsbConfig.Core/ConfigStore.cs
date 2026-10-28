using System.Text.Json;
using UsbDriveManager.Core.Models;

namespace UsbDriveManager.Core;

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// config.json 读写：写入走“临时文件 + 覆盖替换”，避免服务读到半截文件。
/// 注意：按方案约定不做热加载，改完配置必须重启服务。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ConfigStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppPaths.ConfigFile
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            return AppConfig.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return AppConfig.CreateDefault();
            }

            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                         ?? throw new ConfigException("配置文件内容为空。");

            config.Rules ??= new List<DriveRule>();
            foreach (var rule in config.Rules)
            {
                rule.Vid = (rule.Vid ?? "").Trim().ToUpperInvariant();
                rule.Pid = (rule.Pid ?? "").Trim().ToUpperInvariant();
                rule.Serial = SerialRules.Normalize(rule.Serial);
                rule.TargetLetter = char.ToUpperInvariant(rule.TargetLetter);
            }

            return config;
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"配置文件不是合法 JSON：{FilePath}", ex);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"读取配置文件失败：{FilePath}", ex);
        }
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var temp = FilePath + ".tmp";

        File.WriteAllText(temp, json);

        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    /// <summary>首次运行时生成一份空配置（幂等）。</summary>
    public AppConfig LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var config = AppConfig.CreateDefault();
            Save(config);
            return config;
        }

        return Load();
    }
}
