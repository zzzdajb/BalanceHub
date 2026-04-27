using BalanceHub.Core;

namespace BalanceHub.Providers;

/// <summary>
/// 模拟 provider，仅供开发和测试使用。
/// 不调用任何外部 API，直接返回预设的测试数据。
/// 这样可以在不依赖真实第三方服务的情况下，
/// 验证整个程序是否正常工作。
/// </summary>
public class MockProvider : IProvider
{
    /// <summary>此 provider 的类型标识，用于在 TOML 配置中引用。</summary>
    public const string ProviderType = "mock";

    public string Id => "mock";

    public Task<IReadOnlyList<ProviderRecord>> FetchAsync(
        Dictionary<string, object?> config,
        CancellationToken cancellationToken = default)
    {
        // 从配置中读取可自定义的参数，否则使用默认值
        var quotaLimit = GetConfigValue<double?>(config, "quota_limit") ?? 1000;
        var quotaUsage = GetConfigValue<double?>(config, "quota_usage") ?? 150;
        var balance = GetConfigValue<double?>(config, "balance") ?? 5.0;

        // 返回两条测试记录，覆盖两种结果类型
        var records = new List<ProviderRecord>
        {
            new QuotaBasicRecord
            {
                Provider = Id,
                Resource = "key",
                Limit = quotaLimit,
                Usage = quotaUsage,
                Unit = "requests",
            },
            new BalanceBasicRecord
            {
                Provider = Id,
                Resource = "account",
                Balance = balance,
                Unit = "USD",
            },
        };

        return Task.FromResult<IReadOnlyList<ProviderRecord>>(records);
    }

    /// <summary>
    /// 从配置字典中安全地读取指定键的值并转换为目标类型。
    /// Tomlyn 解析后的值类型不固定（可能是 long、double、string 等），
    /// 因此需要做类型转换处理。
    /// </summary>
    private static T? GetConfigValue<T>(Dictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null)
            return default;

        try
        {
            return (T?)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }
}
