using BalanceHub.Core;

namespace BalanceHub.Providers;

/// <summary>
/// Provider 注册表。
/// 根据 type 名称查找对应的 IProvider 实现。
/// 当前 MVP 使用硬编码的内置 provider 列表，未来可以改为动态加载。
/// </summary>
public static class ProviderRegistry
{
    // 所有内置 provider 实例的字典，键为 provider type 名称（小写）
    private static readonly Dictionary<string, IProvider> _providers = new()
    {
        [MockProvider.ProviderType] = new MockProvider(),
    };

    /// <summary>
    /// 根据 type 名称获取 provider 实例。
    /// </summary>
    /// <param name="type">Provider 类型名称，例如 "mock"。</param>
    /// <returns>找到的 provider 实例；未找到则返回 null。</returns>
    public static IProvider? GetProvider(string type)
    {
        _providers.TryGetValue(type.ToLowerInvariant(), out var provider);
        return provider;
    }

    /// <summary>
    /// 获取所有已注册的 provider 类型名称列表。
    /// </summary>
    public static IEnumerable<string> GetProviderTypes() => _providers.Keys;
}
