using System.Text.Json;
using BalanceHub.Core;
using BalanceHub.Providers;
using Tomlyn;
using Tomlyn.Model;

namespace BalanceHub.Cli;

// ============================================================
// 配置文件加载器 — 第 11 节
// 负责读取 balancehub.toml 并解析为 BalanceHubConfig 对象。
// ============================================================

/// <summary>
/// TOML 配置加载器。
/// 读取 balancehub.toml 文件，使用 Tomlyn 库解析为强类型配置对象。
/// 相对路径会基于配置文件所在目录进行解析。
/// </summary>
public class ConfigLoader
{
    /// <summary>
    /// 加载并解析 TOML 配置文件。
    /// </summary>
    /// <param name="configPath">配置文件路径。为 null 时默认使用 "./balancehub.toml"。</param>
    /// <returns>解析后的配置对象。</returns>
    public static BalanceHubConfig Load(string? configPath = null)
    {
        configPath ??= "./balancehub.toml";

        var fullPath = Path.GetFullPath(configPath);
        var configDir = Path.GetDirectoryName(fullPath)!;

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"配置文件未找到: {fullPath}");

        // 使用 Tomlyn 将 TOML 文本解析为强类型配置
        BalanceHubConfig config;
        try
        {
            var tomlContent = File.ReadAllText(fullPath);
            config = TomlSerializer.Deserialize<BalanceHubConfig>(tomlContent, new TomlSerializerOptions
            {
                SourceName = configDir,
                // TOML 文件使用 snake_case 命名，与 C# PascalCase 对应
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            })!; // 反序列化失败时会抛出异常，所以使用 null 包容运算符
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"配置文件格式错误: {ex.Message}", ex);
        }

        // 确保必要字段不为 null
        config.Providers ??= [];
        config.Cache ??= new CacheConfig();

        // 如果缓存目录是相对路径，转换为基于配置文件目录的绝对路径
        var cacheDir = config.Cache.Directory;
        if (!string.IsNullOrEmpty(cacheDir) && !Path.IsPathRooted(cacheDir))
        {
            config.Cache.Directory = Path.GetFullPath(Path.Combine(configDir, cacheDir));
        }

        // 解析所有 provider 的脚本路径和插件目录：相对路径转为绝对路径
        foreach (var provider in config.Providers.Values)
        {
            if (!string.IsNullOrEmpty(provider.Script) && !Path.IsPathRooted(provider.Script))
            {
                provider.Script = Path.GetFullPath(Path.Combine(configDir, provider.Script));
            }

            if (!string.IsNullOrEmpty(provider.Plugin) && !Path.IsPathRooted(provider.Plugin))
            {
                provider.Plugin = Path.GetFullPath(Path.Combine(configDir, provider.Plugin));
            }
        }

        return config;
    }
}

// ============================================================
// JSON 文件缓存管理器 — 第 12 节
// 使用简单的 JSON 文件存储每个 provider 的缓存数据。
// ============================================================

/// <summary>
/// 基于 JSON 文件的缓存管理器。
/// 为每个 provider 维护一个独立的 JSON 缓存文件，
/// 包含获取时间和序列化后的记录数据。
/// </summary>
public class CacheManager
{
    private readonly string _cacheDir;
    private readonly int _ttlSeconds;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public CacheManager(string cacheDir, int ttlSeconds)
    {
        _cacheDir = cacheDir;
        _ttlSeconds = ttlSeconds;
    }

    /// <summary>
    /// 从缓存中读取指定 provider 的数据。
    /// </summary>
    /// <param name="providerId">Provider ID。</param>
    /// <returns>缓存命中且未过期时返回记录列表；否则返回 null。</returns>
    public IReadOnlyList<ProviderRecord>? Read(string providerId)
    {
        var cacheFile = GetCacheFilePath(providerId);
        if (!File.Exists(cacheFile)) return null;

        try
        {
            var json = File.ReadAllText(cacheFile);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json, _jsonOptions);
            if (entry?.Records is null || entry.Records.Count == 0)
                return null;

            // 检查缓存是否过期
            if (DateTimeOffset.TryParse(entry.FetchedAt, out var fetchedAt))
            {
                var age = DateTimeOffset.UtcNow - fetchedAt;
                if (age.TotalSeconds > _ttlSeconds)
                    return null;
            }

            // 标记每条记录为"来自缓存"，保持 fetched_at 不变
            foreach (var record in entry.Records)
            {
                record.Cached = true;
                record.FetchedAt = entry.FetchedAt;
            }

            return entry.Records;
        }
        catch
        {
            return null; // 缓存读取失败等同于未命中
        }
    }

    /// <summary>
    /// 将 provider 数据写入缓存。
    /// </summary>
    public void Write(string providerId, IReadOnlyList<ProviderRecord> records)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var cacheFile = GetCacheFilePath(providerId);

            var entry = new CacheEntry
            {
                FetchedAt = DateTimeOffset.UtcNow.ToString("O"), // ISO 8601 格式
                Records = [.. records],
            };

            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            File.WriteAllText(cacheFile, json);
        }
        catch
        {
            // 缓存写入失败不应影响主流程
        }
    }

    private string GetCacheFilePath(string providerId)
    {
        // 替换文件名中不允许的字符
        var safeName = string.Join("_", providerId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDir, $"{safeName}.json");
    }

    /// <summary>
    /// 缓存条目：存储在 JSON 文件中的内部数据结构。
    /// </summary>
    private class CacheEntry
    {
        public string FetchedAt { get; set; } = "";
        public List<ProviderRecord> Records { get; set; } = [];
    }
}

// ============================================================
// Provider 编排器 — 第 15 节
// 协调 provider 调用、缓存、usage_pct 计算和响应组装。
// ============================================================

/// <summary>
/// Provider 编排器。
/// 负责：读取缓存 → 调用 provider → 写入缓存 → 计算 usage_pct → 组装响应。
/// 这是连接所有服务的核心协调类。
/// </summary>
public class ProviderOrchestrator
{
    private readonly BalanceHubConfig _config;
    private readonly CacheManager _cache;

    public ProviderOrchestrator(BalanceHubConfig config)
    {
        _config = config;
        _cache = new CacheManager(
            config.Cache?.Directory ?? ".balancehub/cache",
            config.Cache?.TtlSeconds ?? 300);
    }

    /// <summary>
    /// 执行数据查询：查询所有启用的 provider，或查询指定 provider。
    /// </summary>
    /// <param name="providerId">可选的 provider ID，指定后只查询该 provider。</param>
    /// <param name="refresh">是否强制刷新（跳过缓存）。</param>
    /// <param name="noCache">是否完全禁用缓存（不读也不写）。</param>
    /// <returns>包含结果数据和错误的响应信封。</returns>
    public async Task<ResponseEnvelope> QueryAsync(
        string? providerId = null,
        bool refresh = false,
        bool noCache = false)
    {
        // 确定要查询哪些 provider 配置
        var providerConfigs = GetTargetProviders(providerId);

        var data = new List<ProviderRecord>();
        var errors = new List<ErrorObject>();
        var cacheEnabled = _config.Cache?.Enabled != false && !noCache;

        foreach (var (id, config) in providerConfigs)
        {
            // 步骤 1：尝试从缓存读取（除非禁用或强制刷新）
            if (cacheEnabled && !refresh)
            {
                var cached = _cache.Read(id);
                if (cached != null)
                {
                    data.AddRange(cached);
                    continue; // 缓存命中，跳过 provider 调用
                }
            }

            // 步骤 2：获取对应 IProvider 实现
            // 优先级: plugin 目录 → script 文件 → 内置 C# provider
            IProvider? provider;
            var scriptPath = ResolvePluginEntry(config.Plugin) ?? config.Script;
            if (!string.IsNullOrEmpty(scriptPath))
            {
                provider = new ScriptProvider(scriptPath, id);
            }
            else
            {
                // 从内置注册表中查找
                var providerType = config.Type ?? id;
                provider = ProviderRegistry.GetProvider(providerType);
            }

            if (provider == null)
            {
                errors.Add(new ErrorObject
                {
                    Provider = id,
                    Code = "provider_not_found",
                    Message = $"未找到 provider 类型: {config.Type ?? id}",
                });
                continue;
            }

            // 步骤 3：将 TOML 配置节转为字典，传递给 provider
            var providerConfigDict = ConvertProviderConfig(id);

            IReadOnlyList<ProviderRecord> records;
            try
            {
                records = await provider.FetchAsync(providerConfigDict);
            }
            catch (Exception ex)
            {
                errors.Add(new ErrorObject
                {
                    Provider = id,
                    Code = "provider_failed",
                    Message = $"{id} 请求失败: {ex.Message}",
                });
                continue;
            }

            // 步骤 4：设置 fetched_at 标记
            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var record in records)
            {
                record.FetchedAt = now;
            }

            // 步骤 5：计算 usage_pct（仅对 quota_basic 类型）
            CalculateUsagePct(records);

            // 步骤 6：写入缓存
            if (cacheEnabled && records.Count > 0)
            {
                _cache.Write(id, records);
            }

            data.AddRange(records);
        }

        // 步骤 7：组装响应信封
        return new ResponseEnvelope
        {
            Ok = errors.Count == 0,
            Data = data,
            Errors = errors,
        };
    }

    /// <summary>
    /// 获取要查询的目标 provider 配置列表。
    /// 如果指定了 providerId，只查找该 provider；否则返回所有已启用的 provider。
    /// </summary>
    private List<KeyValuePair<string, ProviderConfig>> GetTargetProviders(string? providerId)
    {
        if (providerId != null)
        {
            // 查询指定 provider
            if (_config.Providers!.TryGetValue(providerId, out var pc))
            {
                return [new KeyValuePair<string, ProviderConfig>(providerId, pc)];
            }

            return []; // 未找到，由调用方处理错误
        }

        // 查询所有已启用的 provider
        return _config.Providers!
            .Where(p => p.Value.Enabled)
            .ToList();
    }

    /// <summary>
    /// 将 TOML 配置节转换为 provider 可以使用的字典。
    /// 使用 Tomlyn 重新解析该 provider 的配置节以获得完整的键值对。
    /// </summary>
    private static Dictionary<string, object?> ConvertProviderConfig(string providerId)
    {
        // 通过重新解析 TOML 获取 provider 配置节的完整键值对
        // 因为强类型反序列化只提取了已知属性（如 enabled 和 type），
        // provider 特有的字段（如 api_key_env）需要从原始 TOML 中获取
        const string configPath = "./balancehub.toml";
        try
        {
            var tomlContent = File.ReadAllText(configPath);
            var table = TomlSerializer.Deserialize<TomlTable>(tomlContent);
            if (table is null) return [];

            // 导航到 providers.{providerId} 节
            if (table.TryGetValue("providers", out var providersObj) &&
                providersObj is TomlTable providersTable &&
                providersTable.TryGetValue(providerId, out var providerObj) &&
                providerObj is TomlTable providerTable)
            {
                return providerTable.ToDictionary(
                    kv => kv.Key,
                    kv => (object?)kv.Value); // 显式转换以处理可空性
            }
        }
        catch
        {
            // 忽略错误，返回空字典
        }

        return [];
    }

    /// <summary>
    /// 计算所有 quota_basic 记录的 usage_pct。
    /// 公式: usage_pct = usage / limit * 100
    /// 规则：保留两位小数；如果 limit 为 0、负数或无法获取，返回 null。
    /// </summary>
    private static void CalculateUsagePct(IReadOnlyList<ProviderRecord> records)
    {
        foreach (var record in records)
        {
            if (record is QuotaBasicRecord quota)
            {
                if (quota.Limit.HasValue && quota.Usage.HasValue && quota.Limit.Value > 0)
                {
                    quota.UsagePct = Math.Round(quota.Usage.Value / quota.Limit.Value * 100, 2);
                }
                else
                {
                    quota.UsagePct = null;
                }
            }
        }
    }

    /// <summary>
    /// 获取所有已配置且已启用的 provider 摘要信息。
    /// 用于 balancehub list 命令。
    /// </summary>
    public List<object> GetProviderSummary()
    {
        var summaries = new List<object>();
        foreach (var (id, config) in _config.Providers!)
        {
            var providerType = config.Type ?? id;
            var entry = ResolvePluginEntry(config.Plugin) ?? config.Script;
            var hasExternal = !string.IsNullOrEmpty(entry) && File.Exists(entry);
            var hasBuiltIn = ProviderRegistry.GetProvider(providerType) != null;

            summaries.Add(new
            {
                id,
                type = providerType,
                enabled = config.Enabled,
                has_implementation = hasExternal || hasBuiltIn,
            });
        }
        return summaries;
    }

    /// <summary>
    /// 检查配置和 provider 就绪状态。
    /// 用于 balancehub doctor 命令。
    /// </summary>
    public List<object> GetDiagnostics()
    {
        var results = new List<object>();

        // 检查配置是否存在
        results.Add(new
        {
            check = "config_file",
            status = "ok",
            message = "配置已加载",
        });

        // 检查每个已启用 provider 的实现
        foreach (var (id, config) in _config.Providers!)
        {
            if (!config.Enabled) continue;

            // 检查 provider 是否就绪：外部脚本/插件 或 内置 C# 实现
            var entry = ResolvePluginEntry(config.Plugin) ?? config.Script;
            var hasExternal = !string.IsNullOrEmpty(entry) && File.Exists(entry);
            var hasBuiltIn = ProviderRegistry.GetProvider(config.Type ?? id) != null;

            var status = hasExternal || hasBuiltIn ? "ok" : "error";
            var msg = hasExternal
                ? $"Provider '{id}' (插件: {entry}) 已就绪"
                : hasBuiltIn
                    ? $"Provider '{id}' (类型: {config.Type ?? id}) 已就绪"
                    : $"未找到 provider 实现，类型: {config.Type ?? id}";

            results.Add(new { check = $"provider:{id}", status, message = msg });
        }

        return results;
    }

    /// <summary>
    /// 解析插件目录的入口脚本。
    /// 按优先级查找: main.py → main.sh → main.js
    /// </summary>
    /// <param name="pluginDir">插件目录路径，为 null 或空时直接返回 null。</param>
    /// <returns>找到的入口脚本完整路径，未找到时返回 null。</returns>
    private static string? ResolvePluginEntry(string? pluginDir)
    {
        if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
            return null;

        var candidates = new[] { "main.py", "main.sh", "main.js" };
        foreach (var name in candidates)
        {
            var path = Path.Combine(pluginDir, name);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }
}

// ============================================================
// JSON 输出格式化器
// 负责将 ResponseEnvelope 序列化为控制台输出。
// ============================================================

/// <summary>
/// 输出格式化器。
/// 将 ResponseEnvelope 序列化为 JSON 并写入控制台。
/// </summary>
public class OutputFormatter
{
    private static readonly JsonSerializerOptions _prettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions _compactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    /// <summary>
    /// 将响应信封输出为 JSON。
    /// </summary>
    /// <param name="envelope">要输出的响应信封。</param>
    /// <param name="pretty">是否输出格式化后的 JSON（带缩进）。</param>
    public static void WriteOutput(ResponseEnvelope envelope, bool pretty = false)
    {
        var options = pretty ? _prettyOptions : _compactOptions;
        var json = JsonSerializer.Serialize(envelope, options);
        Console.WriteLine(json);
    }
}
