using System.Text.Json;
using BalanceHub.Core;
using BalanceHub.Cli;

// 公共 JSON 序列化选项
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
};

// ============================================================
// BalanceHub CLI 入口 — 第 5 节
//
// 使用手动参数解析（没有依赖外部 CLI 库），
// 这样可以清楚地看到每个参数是如何被处理的。
//
// 命令:
//   balancehub get                         查询所有已启用 provider
//   balancehub get <provider>              查询指定 provider
//   balancehub list                        列出已配置的 provider
//   balancehub doctor                      检查配置和 provider 就绪状态
//
// 选项（适用于 get 命令）:
//   --refresh      强制刷新，跳过缓存
//   --no-cache     完全禁用缓存（不读不写）
//   --config PATH  自定义配置文件路径
//   --pretty       美化 JSON 输出（带缩进）
// ============================================================

// 程序入口
await Main(args);

async Task Main(string[] args)
{
    try
    {
        if (args.Length == 0)
        {
            PrintUsage();
            Environment.ExitCode = 2;
            return;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "get":
                await HandleGet(args[1..]);
                break;
            case "list":
                await HandleList(args[1..]);
                break;
            case "doctor":
                await HandleDoctor(args[1..]);
                break;
            case "--help":
            case "-h":
                PrintUsage();
                break;
            default:
                Console.Error.WriteLine($"未知命令: {command}");
                PrintUsage();
                Environment.ExitCode = 2;
                break;
        }
    }
    catch (Exception ex)
    {
        await WriteErrorAsync("unexpected_error", $"未预期的错误: {ex.Message}");
        Environment.ExitCode = 4;
    }
}

/// <summary>
/// 打印帮助信息。
/// </summary>
void PrintUsage()
{
    Console.WriteLine("BalanceHub — 查询第三方服务的配额和余额信息");
    Console.WriteLine();
    Console.WriteLine("用法:");
    Console.WriteLine("  balancehub get [provider] [选项]   查询配额/余额数据");
    Console.WriteLine("  balancehub list                    列出已配置的 provider");
    Console.WriteLine("  balancehub doctor                  检查配置和 provider 就绪状态");
    Console.WriteLine();
    Console.WriteLine("选项 (get 命令):");
    Console.WriteLine("  --refresh        强制刷新，跳过缓存");
    Console.WriteLine("  --no-cache       完全禁用缓存");
    Console.WriteLine("  --config PATH    自定义配置文件路径");
    Console.WriteLine("  --pretty         美化 JSON 输出");
}

// ============================================================
// 参数解析辅助
// ============================================================

/// <summary>
/// 从参数列表中解析出命名选项（如 --refresh、--config PATH 等）。
/// 返回解析后的选项字典和剩余的位置参数列表。
/// </summary>
( Dictionary<string, string?> options, List<string> positional ) ParseArgs(string[] args)
{
    var options = new Dictionary<string, string?>();
    var positional = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--"))
        {
            var name = args[i][2..]; // 去掉 "--" 前缀
            if (name == "refresh" || name == "no-cache" || name == "pretty")
            {
                // 布尔开关选项，不需要值
                options[name] = "true";
            }
            else if (name == "config")
            {
                // 需要值的选项
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    options[name] = args[++i];
                }
                else
                {
                    // --config 后面没有值，使用默认
                    options[name] = null;
                }
            }
            else
            {
                Console.Error.WriteLine($"未知选项: --{name}");
                Environment.ExitCode = 2;
            }
        }
        else
        {
            // 位置参数（例如 provider ID）
            positional.Add(args[i]);
        }
    }

    return (options, positional);
}

// ============================================================
// 命令处理
// ============================================================

/// <summary>
/// 处理 balancehub get 命令。
/// 解析参数 → 加载配置 → 查询 provider → 输出结果。
/// </summary>
async Task HandleGet(string[] args)
{
    var (options, positional) = ParseArgs(args);

    var providerId = positional.Count > 0 ? positional[0] : null;
    var refresh = options.ContainsKey("refresh");
    var noCache = options.ContainsKey("no-cache");
    var configPath = options.GetValueOrDefault("config");
    var pretty = options.ContainsKey("pretty");

    try
    {
        var config = ConfigLoader.Load(configPath);
        // 解析配置文件所在目录，用于 ConvertProviderConfig 重新读取 TOML
        var configFullPath = Path.GetFullPath(configPath ?? "./balancehub.toml");
        var configDir = Path.GetDirectoryName(configFullPath)!;
        var orchestrator = new ProviderOrchestrator(config, configDir);

        // 如果指定了 provider ID，验证配置中是否存在
        if (providerId != null && !config.Providers!.ContainsKey(providerId))
        {
            OutputFormatter.WriteOutput(new ResponseEnvelope
            {
                Ok = false,
                Errors =
                [
                    new ErrorObject
                    {
                        Code = "provider_not_found",
                        Message = $"配置中未找到 provider: {providerId}",
                    },
                ],
            }, pretty);
            Environment.ExitCode = 3;
            return;
        }

        var result = await orchestrator.QueryAsync(providerId, refresh, noCache);
        OutputFormatter.WriteOutput(result, pretty);

        // 退出码: 0=全部成功, 1=部分或全部失败
        Environment.ExitCode = result.Ok ? 0 : 1;
    }
    catch (FileNotFoundException ex)
    {
        OutputFormatter.WriteOutput(new ResponseEnvelope
        {
            Ok = false,
            Errors =
            [
                new ErrorObject { Code = "config_not_found", Message = ex.Message },
            ],
        }, pretty);
        Environment.ExitCode = 3;
    }
    catch (InvalidDataException ex)
    {
        OutputFormatter.WriteOutput(new ResponseEnvelope
        {
            Ok = false,
            Errors =
            [
                new ErrorObject { Code = "invalid_config", Message = ex.Message },
            ],
        }, pretty);
        Environment.ExitCode = 3;
    }
}

/// <summary>
/// 处理 balancehub list 命令。
/// 列出配置文件中所有已配置的 provider。
/// </summary>
async Task HandleList(string[] args)
{
    var (options, positional) = ParseArgs(args);
    var configPath = options.GetValueOrDefault("config");

    try
    {
        var config = ConfigLoader.Load(configPath);
        var orchestrator = new ProviderOrchestrator(config);
        var summaries = orchestrator.GetProviderSummary();

        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            providers = summaries,
        }, jsonOptions);
        Console.WriteLine(json);
        Environment.ExitCode = 0;
    }
    catch (FileNotFoundException ex)
    {
        await WriteErrorAsync("config_not_found", ex.Message);
        Environment.ExitCode = 3;
    }
}

/// <summary>
/// 处理 balancehub doctor 命令。
/// 检查配置文件是否有效、每个已启用 provider 的实现是否就绪。
/// </summary>
async Task HandleDoctor(string[] args)
{
    var (options, positional) = ParseArgs(args);
    var configPath = options.GetValueOrDefault("config");

    try
    {
        var config = ConfigLoader.Load(configPath);
        var orchestrator = new ProviderOrchestrator(config);
        var diagnostics = orchestrator.GetDiagnostics();

        var hasErrors = diagnostics.Any(d =>
        {
            var prop = d.GetType().GetProperty("status");
            return prop?.GetValue(d)?.ToString() == "error";
        });

        var json = JsonSerializer.Serialize(new
        {
            ok = !hasErrors,
            checks = diagnostics,
        }, jsonOptions);
        Console.WriteLine(json);
        Environment.ExitCode = hasErrors ? 1 : 0;
    }
    catch (FileNotFoundException ex)
    {
        // 配置文件不存在是 doctor 需要报告的情况，属于检查结果而非崩溃
        var json = JsonSerializer.Serialize(new
        {
            ok = false,
            checks = new[]
            {
                new { check = "config_file", status = "error", message = ex.Message },
            },
        }, jsonOptions);
        Console.WriteLine(json);
        Environment.ExitCode = 1;
    }
}

/// <summary>
/// 以标准 JSON 格式输出错误信息。
/// </summary>
async Task WriteErrorAsync(string code, string message)
{
    var json = JsonSerializer.Serialize(new
    {
        ok = false,
        error = new { code, message },
    }, jsonOptions);
    Console.WriteLine(json);
}
