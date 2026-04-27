using System.Text.Json;
using BalanceHub.Core;

namespace BalanceHub.Cli.Tests;

/// <summary>
/// CLI 服务的集成测试。
/// 使用 mock provider 验证完整的 get 命令工作流。
/// </summary>
public class ServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _cacheDir;

    public ServiceTests()
    {
        // 为每次测试创建临时目录
        _tempDir = Path.Combine(Path.GetTempPath(), $"balancehub_test_{Guid.NewGuid()}");
        _cacheDir = Path.Combine(_tempDir, ".balancehub", "cache");
        _configPath = Path.Combine(_tempDir, "balancehub.toml");

        Directory.CreateDirectory(_cacheDir);

        // 写入测试配置文件
        File.WriteAllText(_configPath, @"
[cache]
enabled = true
ttl_seconds = 300
directory = "".balancehub/cache""

[providers.mock]
enabled = true
type = ""mock""
");
    }

    public void Dispose()
    {
        // 测试结束后清理临时文件
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// 验证完整的 get 流程：加载配置 → 查询 mock → 返回正确格式的结果。
    /// </summary>
    [Fact]
    public async Task GetCommand_WithMockProvider_ReturnsValidResponse()
    {
        var config = ConfigLoader.Load(_configPath);
        var orchestrator = new ProviderOrchestrator(config);

        var result = await orchestrator.QueryAsync();

        // 验证响应结构
        Assert.True(result.Ok);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Data.Count);

        // 验证包含 quota_basic 记录
        var quota = result.Data.OfType<QuotaBasicRecord>().FirstOrDefault();
        Assert.NotNull(quota);
        Assert.Equal("mock", quota.Provider);
        Assert.Equal("key", quota.Resource);
        Assert.Equal(1000, quota.Limit);
        Assert.Equal(150, quota.Usage);
        Assert.Equal(15.0, quota.UsagePct); // 150 / 1000 * 100 = 15.0
        Assert.Equal("requests", quota.Unit);

        // 验证包含 balance_basic 记录
        var balance = result.Data.OfType<BalanceBasicRecord>().FirstOrDefault();
        Assert.NotNull(balance);
        Assert.Equal("mock", balance.Provider);
        Assert.Equal("account", balance.Resource);
        Assert.Equal(5.0, balance.Balance);
        Assert.Equal("USD", balance.Unit);

        // 验证 fetched_at 已被设置
        Assert.False(string.IsNullOrEmpty(quota.FetchedAt));
        Assert.False(quota.Cached);
    }

    /// <summary>
    /// 验证 usage_pct 的计算：usage=0 时返回 0。
    /// </summary>
    [Fact]
    public void UsagePct_WithZeroUsage_ReturnsZero()
    {
        var record = new QuotaBasicRecord { Limit = 1000, Usage = 0 };
        // 手动调用计算逻辑（来自 ProviderOrchestrator）
        record.UsagePct = record.Limit > 0
            ? Math.Round(record.Usage.Value / record.Limit.Value * 100, 2)
            : null;

        Assert.Equal(0, record.UsagePct);
    }

    /// <summary>
    /// 验证 usage_pct 的计算：limit=0 时返回 null。
    /// </summary>
    [Fact]
    public void UsagePct_WithZeroLimit_ReturnsNull()
    {
        var record = new QuotaBasicRecord { Limit = 0, Usage = 150 };
        double? usagePct = record.Limit > 0
            ? Math.Round(record.Usage.Value / record.Limit.Value * 100, 2)
            : null;

        Assert.Null(usagePct);
    }

    /// <summary>
    /// 验证 list 命令返回已配置的 provider 列表。
    /// </summary>
    [Fact]
    public void ListCommand_ReturnsProviderList()
    {
        var config = ConfigLoader.Load(_configPath);
        var orchestrator = new ProviderOrchestrator(config);

        var summaries = orchestrator.GetProviderSummary();

        Assert.NotEmpty(summaries);
        // 使用 JSON 序列化来验证格式
        var json = JsonSerializer.Serialize(summaries,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Contains("mock", json);
    }

    /// <summary>
    /// 验证 doctor 命令的诊断结果包含配置检查。
    /// </summary>
    [Fact]
    public void DoctorCommand_ReportsConfigStatus()
    {
        var config = ConfigLoader.Load(_configPath);
        var orchestrator = new ProviderOrchestrator(config);

        var diagnostics = orchestrator.GetDiagnostics();

        Assert.NotEmpty(diagnostics);
        // 至少应该有一个 config_file 检查
        Assert.Contains(diagnostics, d =>
        {
            var prop = d.GetType().GetProperty("check");
            return prop?.GetValue(d)?.ToString() == "config_file";
        });
    }

    /// <summary>
    /// 验证配置文件不存在时 ConfigLoader 抛出 FileNotFoundException。
    /// </summary>
    [Fact]
    public void ConfigLoader_MissingFile_Throws()
    {
        var fakePath = Path.Combine(_tempDir, "nonexistent.toml");
        Assert.Throws<FileNotFoundException>(() => ConfigLoader.Load(fakePath));
    }
}
