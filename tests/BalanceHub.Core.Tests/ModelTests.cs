using System.Text.Json;
using BalanceHub.Core;

namespace BalanceHub.Core.Tests;

/// <summary>
/// 核心模型的序列化和逻辑测试。
/// </summary>
public class ModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// 验证 QuotaBasicRecord 序列化后包含所有必要字段。
    /// </summary>
    [Fact]
    public void QuotaBasicRecord_Serializes_WithAllFields()
    {
        var record = new QuotaBasicRecord
        {
            Provider = "mock",
            Resource = "key",
            Limit = 1000,
            Usage = 150,
            UsagePct = 15.0,
            Unit = "requests",
            FetchedAt = "2026-04-27T12:00:00Z",
        };

        // 使用基类类型序列化以触发多态 type 字段
        var json = JsonSerializer.Serialize<ProviderRecord>(record, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("mock", doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal("key", doc.RootElement.GetProperty("resource").GetString());
        Assert.Equal("quota_basic", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(1000, doc.RootElement.GetProperty("limit").GetDouble());
        Assert.Equal(150, doc.RootElement.GetProperty("usage").GetDouble());
        Assert.Equal(15.0, doc.RootElement.GetProperty("usage_pct").GetDouble());
        Assert.Equal("requests", doc.RootElement.GetProperty("unit").GetString());
    }

    /// <summary>
    /// 验证 BalanceBasicRecord 序列化后包含所有必要字段。
    /// </summary>
    [Fact]
    public void BalanceBasicRecord_Serializes_WithAllFields()
    {
        var record = new BalanceBasicRecord
        {
            Provider = "perplexity",
            Resource = "account",
            Balance = 5.0,
            Unit = "USD",
            FetchedAt = "2026-04-27T12:00:00Z",
        };

        // 使用基类类型序列化以触发多态 type 字段
        var json = JsonSerializer.Serialize<ProviderRecord>(record, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("perplexity", doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal("account", doc.RootElement.GetProperty("resource").GetString());
        Assert.Equal("balance_basic", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(5.0, doc.RootElement.GetProperty("balance").GetDouble());
        Assert.Equal("USD", doc.RootElement.GetProperty("unit").GetString());
    }

    /// <summary>
    /// 验证响应信封的序列化格式正确。
    /// </summary>
    [Fact]
    public void ResponseEnvelope_Serializes_Correctly()
    {
        var envelope = new ResponseEnvelope
        {
            Ok = true,
            Data =
            [
                new QuotaBasicRecord
                {
                    Provider = "mock",
                    Resource = "key",
                    Limit = 1000,
                    Usage = 150,
                    Unit = "requests",
                },
            ],
            Errors = [],
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("errors").EnumerateArray());
    }

    /// <summary>
    /// 验证 ErrorObject 的序列化。
    /// </summary>
    [Fact]
    public void ErrorObject_Serializes_Correctly()
    {
        var error = new ErrorObject
        {
            Provider = "tavily",
            Code = "provider_failed",
            Message = "请求失败",
        };

        var json = JsonSerializer.Serialize(error, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("tavily", doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal("provider_failed", doc.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// 验证 usage_pct 的多态序列化（type 字段通过 JsonDerivedType 自动生成）。
    /// </summary>
    [Fact]
    public void ProviderRecord_Polymorphic_Serialization()
    {
        // 测试基类引用指向子类时，type 字段是否正确
        ProviderRecord record = new QuotaBasicRecord
        {
            Provider = "test",
            Resource = "key",
            Limit = 100,
            Usage = 30,
        };

        var json = JsonSerializer.Serialize(record, JsonOptions);
        var doc = JsonDocument.Parse(json);

        // type 字段应当由 JsonDerivedType 自动写入
        Assert.Equal("quota_basic", doc.RootElement.GetProperty("type").GetString());
    }

    /// <summary>
    /// 验证多态反序列化：序列化后再反序列化应还原为正确的子类。
    /// </summary>
    [Fact]
    public void ProviderRecord_Polymorphic_Deserialization()
    {
        // 先序列化一个 QuotaBasicRecord
        ProviderRecord original = new QuotaBasicRecord
        {
            Provider = "test",
            Resource = "key",
            Limit = 100,
            Usage = 30,
            Unit = "requests",
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);

        // 再反序列化回 ProviderRecord
        var record = JsonSerializer.Deserialize<ProviderRecord>(json, JsonOptions);
        Assert.NotNull(record);
        Assert.IsType<QuotaBasicRecord>(record);

        var quota = (QuotaBasicRecord)record;
        Assert.Equal("test", quota.Provider);
        Assert.Equal("key", quota.Resource);
        Assert.Equal(100, quota.Limit);
        Assert.Equal(30, quota.Usage);
    }
}
