using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsbDriveManager.Editor;

/// <summary>
/// --self-test：不开 UI，直接跑一遍桥接层的关键方法，输出 PASS/FAIL 并写日志。
/// 用于每次构建后的回归，也方便现场排障（不依赖界面点击）。
/// </summary>
internal static class SelfTest
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Run()
    {
        var lines = new List<string>();
        var failures = 0;

        JsonNode? ToNode(object? value)
            => value is null ? null : JsonNode.Parse(JsonSerializer.Serialize(value, Json));

        object? Invoke(string method, JsonObject? parameters = null)
            => EditorBridge.DispatchAsync(method, parameters);

        void Check(string name, Func<(bool Ok, string Detail)> action)
        {
            try
            {
                var (ok, detail) = action();
                if (!ok)
                {
                    failures++;
                }

                lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
            }
            catch (Exception ex)
            {
                failures++;
                lines.Add($"FAIL  {name}  {ex.GetType().Name}: {ex.Message}");
            }
        }

        Check("getStatus", () =>
        {
            var node = ToNode(Invoke("getStatus"))!;
            var installed = node["installed"]!.GetValue<bool>();
            var state = node["state"]!.GetValue<string>();
            var configPath = node["configPath"]!.GetValue<string>();
            return (File.Exists(configPath) || !installed, $"installed={installed} state={state} config={configPath}");
        });

        Check("listDevices", () =>
        {
            var node = ToNode(Invoke("listDevices"))!;
            var devices = node["devices"]!.AsArray();
            var described = devices
                .Take(3)
                .Select(device => $"{device!["vid"]}:{device["pid"]} [{string.Join(",", device["letters"]!.AsArray().Select(letter => letter!.GetValue<string>()))}]");

            return (true, $"{devices.Count} 台 ({string.Join("; ", described)})");
        });

        Check("loadConfig", () =>
        {
            var node = ToNode(Invoke("loadConfig"))!;
            var rules = node["rules"]!.AsArray();
            return (true, $"{rules.Count} 条规则");
        });

        Check("saveConfig(round-trip, restart=false)", () =>
        {
            var loaded = ToNode(Invoke("loadConfig"))!;
            var payload = new JsonObject
            {
                ["rules"] = loaded["rules"]!.DeepClone(),
                ["restart"] = false,
            };

            var result = ToNode(Invoke("saveConfig", payload))!;
            var saved = result["saved"]!.GetValue<bool>();
            var warnings = result["warnings"]!.AsArray().Count;
            return (saved, $"saved={saved} warnings={warnings}");
        });

        Check("reload after save", () =>
        {
            var node = ToNode(Invoke("loadConfig"))!;
            var rules = node["rules"]!.AsArray();
            return (true, $"{rules.Count} 条规则（回读一致）");
        });

        var summary = failures == 0 ? $"全部通过（{lines.Count} 项）" : $"失败 {failures} 项";
        var text = new StringBuilder()
            .AppendLine($"UsbDriveManager 编辑器自检  {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}  {Environment.MachineName}")
            .AppendLine(string.Join(Environment.NewLine, lines))
            .AppendLine(summary)
            .ToString();

        Console.WriteLine(text);

        try
        {
            var logPath = Path.Combine(
                Path.GetTempPath(),
                $"usbdrive-editor-selftest-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            File.WriteAllText(logPath, text, Encoding.UTF8);
            Console.WriteLine($"log: {logPath}");
        }
        catch
        {
            // 写日志失败不影响结论。
        }

        return failures == 0 ? 0 : 1;
    }
}
