using System.Diagnostics;
using System.Text.Json;
using BalanceHub.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using JsonSubTypes;

namespace BalanceHub.Providers;

/// <summary>
/// 脚本 provider —— 通过执行外部脚本来获取配额/余额数据。
///
/// 这是 BalanceHub 的主要插件机制。任何人都可以用自己熟悉的语言
/// （Bash、Python、Node.js、PowerShell 等）编写 provider，
/// 只需要遵守以下约定：
///
/// 输入（通过环境变量）:
///   BALANCEHUB_CONFIG — provider 配置的 JSON 字符串，包含 TOML 文件中
///                       该 provider 节的所有键值对（如 api_key 等）
///
/// 输出（stdout）:
///   符合 ProviderRecord JSON 格式的数组（必须输出到标准输出）
///
/// 退出码:
///   0  — 成功
///   非 0 — 失败（stderr 内容将作为错误消息上报）
///
/// 示例脚本见 plugins/ 目录。
/// </summary>
public class ScriptProvider : IProvider
{
    private readonly string _scriptPath;
    private static readonly JsonSerializerOptions _stjOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Newtonsoft.Json 的序列化设置，用于解析脚本输出
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Converters =
        {
            JsonSubTypes.JsonSubtypesConverterBuilder
                .Of(typeof(ProviderRecord), "type")
                .RegisterSubtype(typeof(QuotaBasicRecord), "quota_basic")
                .RegisterSubtype(typeof(BalanceBasicRecord), "balance_basic")
                .Build(),
        },
    };

    /// <param name="scriptPath">脚本文件的绝对路径。</param>
    /// <param name="id">Provider ID。</param>
    public ScriptProvider(string scriptPath, string id)
    {
        _scriptPath = scriptPath;
        Id = id;
    }

    public string Id { get; }

    public async Task<IReadOnlyList<ProviderRecord>> FetchAsync(
        Dictionary<string, object?> config,
        CancellationToken cancellationToken = default)
    {
        // 1. 检查脚本是否存在
        if (!File.Exists(_scriptPath))
            throw new InvalidOperationException($"脚本未找到: {_scriptPath}");

        // 2. 检查脚本是否可执行（非 Windows 系统检查 x 权限）
        if (!OperatingSystem.IsWindows())
        {
            var fileInfo = new FileInfo(_scriptPath);
            if ((fileInfo.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                throw new InvalidOperationException($"路径是目录而非脚本: {_scriptPath}");
        }

        // 3. 序列化配置为 JSON，通过环境变量传递给脚本
        var configJson = System.Text.Json.JsonSerializer.Serialize(config, _stjOptions);

        var scriptDir = Path.GetDirectoryName(_scriptPath)!;

        var startInfo = new ProcessStartInfo
        {
            FileName = _scriptPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = scriptDir,
        };
        startInfo.EnvironmentVariables["BALANCEHUB_CONFIG"] = configJson;

        // 4. 执行脚本
        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法执行脚本: {ex.Message}", ex);
        }

        // 并发读取 stdout 和 stderr，防止死锁
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var output = await stdoutTask;
        var error = await stderrTask;

        // 5. 检查退出码
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrEmpty(error) ? output : error;
            throw new InvalidOperationException(
                $"脚本退出码 {process.ExitCode}: {detail.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(output))
            return [];

        // 6. 解析 stdout 的 JSON 输出
        // 使用 Newtonsoft.Json + JsonSubtypes 反序列化，
        // type 分派字段可以在 JSON 对象的任意位置。
        try
        {
            return JsonConvert.DeserializeObject<List<ProviderRecord>>(output, _jsonSettings) ?? [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"脚本输出不是有效的 JSON: {ex.Message}\n输出内容: {output.Trim()[..Math.Min(output.Length, 200)]}",
                ex);
        }
    }
}
