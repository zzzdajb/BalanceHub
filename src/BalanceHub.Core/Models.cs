using System.Text.Json.Serialization;

namespace BalanceHub.Core;

// ============================================================
// 第 6-9 节：结果类型和响应信封
// 所有 CLI 返回的数据都使用相同的顶层响应格式。
// ============================================================

/// <summary>
/// 所有 provider 返回结果的基类。
/// 使用 JsonPolymorphic + JsonDerivedType 实现多态序列化：
/// 序列化时自动写入 "type": "quota_basic" 或 "type": "balance_basic"，
/// 反序列化时也根据 "type" 字段创建正确的子类实例。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(QuotaBasicRecord), typeDiscriminator: "quota_basic")]
[JsonDerivedType(typeof(BalanceBasicRecord), typeDiscriminator: "balance_basic")]
public abstract record ProviderRecord
{
    /// <summary>小写 provider ID，例如 "tavily"。</summary>
    public string Provider { get; init; } = "";

    /// <summary>该 provider 内的小写资源 ID，例如 "key"。</summary>
    public string Resource { get; init; } = "";

    /// <summary>数据实际从 provider 获取时的 ISO 8601 时间戳。</summary>
    public string FetchedAt { get; set; } = "";

    /// <summary>当前结果是否来自缓存。</summary>
    public bool Cached { get; set; }
}

/// <summary>
/// 配额使用量（quota_basic）。
/// 表示已使用量相对固定上限的消耗情况，例如 "已使用 150 次，上限 1000 次"。
/// </summary>
public record QuotaBasicRecord : ProviderRecord
{
    /// <summary>总配额上限。</summary>
    public double? Limit { get; init; }

    /// <summary>已使用的配额。</summary>
    public double? Usage { get; init; }

    /// <summary>
    /// 使用百分比，由 BalanceHub 主程序计算，保留两位小数。
    /// 公式: usage_pct = usage / limit * 100
    /// </summary>
    public double? UsagePct { get; set; }

    /// <summary>配额单位，例如 "requests"；可能为 null。</summary>
    public string? Unit { get; init; }
}

/// <summary>
/// 余额结果（balance_basic）。
/// 表示简单的剩余余额或积分值，例如 "$5.00 USD"。
/// </summary>
public record BalanceBasicRecord : ProviderRecord
{
    /// <summary>当前余额数值。</summary>
    public double? Balance { get; init; }

    /// <summary>余额单位，例如 "USD"；可能为 null。</summary>
    public string? Unit { get; init; }
}

/// <summary>
/// 所有返回结构化数据的命令使用的顶层响应信封。
/// </summary>
public record ResponseEnvelope
{
    /// <summary>命令完全成功时为 true；有任何 provider 失败时为 false。</summary>
    public bool Ok { get; init; }

    /// <summary>成功的 provider 结果列表。即使 ok=false 也可能包含部分数据。</summary>
    public List<ProviderRecord> Data { get; init; } = [];

    /// <summary>错误列表。无错误时为空数组。</summary>
    public List<ErrorObject> Errors { get; init; } = [];
}

/// <summary>
/// 结构化的错误对象。
/// </summary>
public record ErrorObject
{
    /// <summary>
    /// 如果错误与特定 provider 相关，则为该 provider 的 ID；
    /// 否则为 null（例如配置错误）。
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>稳定的机器可读错误码，例如 "config_not_found"。</summary>
    public string Code { get; init; } = "";

    /// <summary>简短的人类可读错误说明。</summary>
    public string Message { get; init; } = "";
}

// ============================================================
// 第 11 节：TOML 配置模型
// ============================================================

/// <summary>
/// 从 TOML 配置文件反序列化的顶层配置对象。
/// </summary>
public class BalanceHubConfig
{
    /// <summary>缓存配置节。</summary>
    public CacheConfig? Cache { get; set; }

    /// <summary>Provider 配置字典，键为 provider ID。</summary>
    public Dictionary<string, ProviderConfig>? Providers { get; set; }
}

/// <summary>
/// 缓存配置。
/// </summary>
public class CacheConfig
{
    /// <summary>是否启用缓存。默认 true。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>缓存有效期（秒）。默认 300（5 分钟）。</summary>
    public int TtlSeconds { get; set; } = 300;

    /// <summary>缓存目录路径。相对路径基于配置文件所在目录。</summary>
    public string? Directory { get; set; } = ".balancehub/cache";
}

/// <summary>
/// 单个 provider 的配置节。
/// 例如 [providers.tavily] 中的 enabled 和 type 字段。
/// 如果 script 字段非空，表示这是一个外部脚本 provider；
/// 否则使用内置的 C# provider 实现。
/// 其他 provider 特有的配置项（如 api_key_env）通过 Tomlyn 的 TomlTable 索引器读取，
/// 不在本类中预先定义。
/// </summary>
public class ProviderConfig
{
    /// <summary>是否启用该 provider。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Provider 类型标识，用于在 ProviderRegistry 中查找对应的实现。</summary>
    public string? Type { get; set; }

    /// <summary>外部脚本路径。设置此项后，BalanceHub 会执行该脚本而非调用内置 C# provider。</summary>
    public string? Script { get; set; }
}

// ============================================================
// 第 15 节：Provider 接口
// ============================================================

/// <summary>
/// Provider 实现必须实现的接口。
/// 每个 provider 知道如何从特定的第三方服务获取配额/余额数据，
/// 并返回规范化的 ProviderRecord 列表。
///
/// Provider 不应计算 usage_pct 或处理缓存逻辑——这些由主程序负责。
/// </summary>
public interface IProvider
{
    /// <summary>小写且稳定的 provider ID，例如 "tavily"。</summary>
    string Id { get; }

    /// <summary>
    /// 从外部服务获取配额或余额数据。
    /// </summary>
    /// <param name="config">该 provider 的配置键值对（来自 TOML 文件）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>规范化的 provider 记录列表（不含 fetched_at 和 usage_pct）。</returns>
    Task<IReadOnlyList<ProviderRecord>> FetchAsync(
        Dictionary<string, object?> config,
        CancellationToken cancellationToken = default);
}
