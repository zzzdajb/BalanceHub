# BalanceHub — Agent/LLM 使用手册

## 简介

BalanceHub 是一个 CLI 工具，用于查询第三方服务的配额和余额信息。
所有输出均为 JSON，便于 LLM、agent、脚本直接解析。

## 命令参考

### balancehub get

查询所有已启用 provider 的配额/余额数据。

```bash
balancehub get
balancehub get --pretty
```

查询指定 provider：

```bash
balancehub get tavily
balancehub get tavily --pretty
```

选项：

| 选项 | 效果 |
|---|---|
| `--refresh` | 跳过缓存，强制获取最新数据 |
| `--no-cache` | 不读也不写缓存 |
| `--config <path>` | 使用自定义配置文件 |
| `--pretty` | 美化 JSON 输出（默认是紧凑格式） |

### balancehub list

列出配置文件中所有已配置的 provider。

```bash
balancehub list
```

### balancehub doctor

检查配置和 provider 实现是否就绪。

```bash
balancehub doctor
```

## 输出格式

所有返回数据的命令使用统一的 JSON 响应信封。

### 成功响应

```json
{
  "ok": true,
  "data": [
    {
      "type": "quota_basic",
      "provider": "tavily",
      "resource": "key",
      "limit": 1000,
      "usage": 150,
      "usage_pct": 15.0,
      "unit": "requests",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": false
    }
  ],
  "errors": []
}
```

### 部分失败响应

```json
{
  "ok": false,
  "data": [
    {
      "type": "balance_basic",
      "provider": "perplexity",
      "resource": "account",
      "balance": 5.0,
      "unit": "USD",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": false
    }
  ],
  "errors": [
    {
      "provider": "tavily",
      "code": "provider_failed",
      "message": "Tavily quota request failed."
    }
  ]
}
```

### list 输出

```json
{
  "ok": true,
  "providers": [
    {
      "id": "mock",
      "type": "mock",
      "enabled": true,
      "has_implementation": true
    }
  ]
}
```

### doctor 输出

```json
{
  "ok": true,
  "checks": [
    { "check": "config_file", "status": "ok", "message": "..." },
    { "check": "provider:mock", "status": "ok", "message": "..." }
  ]
}
```

## 退出码

| 码 | 含义 | 典型场景 |
|---|---|---|
| 0 | 全部成功 | 所有 provider 查询成功 |
| 1 | 部分失败 | 某个 provider 查询失败，但其他成功 |
| 2 | 参数错误 | 未知命令或选项 |
| 3 | 配置错误 | 配置文件不存在或格式错误 |
| 4 | 未预期错误 | 系统异常 |

JSON 输出在非零退出码时仍会尽可能输出。

## 结果类型

### quota_basic

配额使用量，包含 `usage_pct` 自动计算。

字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `provider` | string | Provider ID |
| `resource` | string | 资源 ID（如 `key`、`account_plan`） |
| `type` | string | 固定为 `"quota_basic"` |
| `limit` | number or null | 配额上限 |
| `usage` | number or null | 已使用量 |
| `usage_pct` | number or null | 使用百分比（保留 2 位小数） |
| `unit` | string or null | 单位（如 `"requests"`） |
| `fetched_at` | string | ISO 8601 时间戳 |
| `cached` | boolean | 是否来自缓存 |

`usage_pct` 由 BalanceHub 自动计算，公式为 `usage / limit * 100`。
当 `limit` 为 0、负数或无法获取时返回 `null`。

### balance_basic

余额值。

字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `provider` | string | Provider ID |
| `resource` | string | 资源 ID（如 `"account"`） |
| `type` | string | 固定为 `"balance_basic"` |
| `balance` | number or null | 余额数值 |
| `unit` | string or null | 货币单位（如 `"USD"`） |
| `fetched_at` | string | ISO 8601 时间戳 |
| `cached` | boolean | 是否来自缓存 |

## 错误码

| 错误码 | 含义 |
|---|---|
| `invalid_args` | 无效参数 |
| `config_not_found` | 配置文件不存在 |
| `invalid_config` | 配置文件格式错误 |
| `provider_not_found` | 配置中未找到指定 provider |
| `provider_disabled` | Provider 已禁用 |
| `missing_config` | 缺少必要配置项 |
| `auth_failed` | 认证失败 |
| `network_failed` | 网络请求失败 |
| `parse_failed` | 数据解析失败 |
| `provider_failed` | Provider 请求异常 |
| `cache_failed` | 缓存读写失败 |
| `unexpected_error` | 未预期错误 |

## 缓存行为

默认启用缓存，TTL 为 300 秒（5 分钟）。

| 模式 | 读缓存 | 写缓存 |
|---|---|---|
| 默认 | ✅ 读 | ✅ 写 |
| `--refresh` | ❌ 不读 | ✅ 写 |
| `--no-cache` | ❌ 不读 | ❌ 不写 |

每次返回的每条记录都包含 `fetched_at`（数据实际获取时间）和 `cached`（是否命中缓存），
便于 agent 自行判断数据新鲜度。

## 配置文件

默认路径 `./balancehub.toml`，可使用 `--config` 指定其他路径。

```toml
[cache]
enabled = true
ttl_seconds = 300
directory = ".balancehub/cache"

[providers.tavily-python]
enabled = true
type = "tavily"
plugin = "./plugins/tavily-python"
api_key = "sk-tavily-xxxxx"
```

- API 密钥直接写在配置文件中（`balancehub.toml` 默认已被 .gitignore 排除）
- Cookie 文件通过 `cookie_file` 字段引用独立文件
- 相对路径基于配置文件所在目录解析

## 示例 — 完整流程

```bash
# 1. 从模板创建配置文件
cp balancehub.toml.example balancehub.toml

# 2. 诊断检查
balancehub doctor

# 3. 查询所有 provider（紧凑 JSON，适合管道处理）
balancehub get

# 4. 美化输出（适合人工查看）
balancehub get --pretty

# 5. 强制获取最新数据
balancehub get --refresh

# 6. 只查一个 provider
balancehub get mock

# 7. 使用自定义配置
balancehub get --config ./production.toml
```
