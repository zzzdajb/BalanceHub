# BalanceHub 插件开发指南

## 概述

BalanceHub 的插件机制建立在 **外部脚本** 之上。
任何人都可以用自己熟悉的语言（Bash、Python、Node.js、PowerShell、Ruby 等）
编写 provider 脚本，只要遵守输入输出合约即可。

这意味着你**不需要会 C#** 也能为 BalanceHub 编写 provider。

## 插件合约

一个 provider 脚本就是一个可执行文件，它和 BalanceHub 之间通过以下方式通信：

```
┌─────────────────┐     环境变量 BALANCEHUB_CONFIG     ┌────────────────┐
│                 │ ──────────────────────────────────► │                │
│   BalanceHub    │      stdout: JSON 数组              │  Provider 脚本 │
│   (主程序)       │ ◄────────────────────────────────── │  (任意语言)     │
│                 │     退出码: 0 成功 / 非0 失败        │                │
└─────────────────┘                                     └────────────────┘
```

### 输入

BalanceHub 将 TOML 配置中该 provider 节的所有键值对序列化为 JSON，
通过环境变量 `BALANCEHUB_CONFIG` 传递给脚本。

例如，对于以下配置：

```toml
[providers.tavily]
enabled = true
type = "tavily"
script = "./scripts/tavily.sh"
api_key_env = "TAVILY_API_KEY"
```

脚本收到的 `BALANCEHUB_CONFIG` 环境变量值为：

```json
{
  "enabled": true,
  "type": "tavily",
  "script": "./scripts/tavily.sh",
  "api_key_env": "TAVILY_API_KEY"
}
```

API 密钥通过**环境变量**传递（不写入配置文件）：

```bash
export TAVILY_API_KEY="sk-..."
```

脚本读取 `BALANCEHUB_CONFIG` 获取环境变量名，然后通过 `"${!API_KEY_ENV}"`
或 `os.environ.get(api_key_env)` 获取实际密钥。

### 输出

脚本必须向 **stdout** 输出符合以下 JSON 格式的数组：

```json
[
  {
    "type": "quota_basic",
    "provider": "tavily",
    "resource": "key",
    "limit": 1000,
    "usage": 150,
    "unit": "requests"
  },
  {
    "type": "balance_basic",
    "provider": "tavily",
    "resource": "account",
    "balance": 5.0,
    "unit": "USD"
  }
]
```

### 退出码

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 非 0 | 失败（stderr 内容将作为错误消息上报） |

### stderr

stderr 可用于输出调试日志。退出码为 0 时 BalancedHub 忽略 stderr；退出码非 0 时 stderr 内容会作为错误信息上报。

## 合约速查表

| 项目 | 要求 |
|---|---|
| 输入 | `BALANCEHUB_CONFIG` 环境变量（JSON 字符串） |
| 输出 | stdout — JSON 数组，元素必须包含 `type`、`provider`、`resource` 字段 |
| 成功退出码 | 0 |
| 失败退出码 | 非 0（将 stderr 作为错误消息） |
| 依赖 | 脚本所需运行时需预装（bash、python3、node 等） |
| 权限 | 需要执行权限（`chmod +x`） |

## 快速入门示例

### Bash 版（无需额外依赖）

```bash
#!/usr/bin/env bash
set -euo pipefail

# 1. 读取配置
API_KEY_ENV=$(echo "$BALANCEHUB_CONFIG" | python3 -c "
import sys, json
print(json.load(sys.stdin).get('api_key_env', ''))
")
API_KEY="${!API_KEY_ENV:-}"

if [ -z "$API_KEY" ]; then
    echo "错误: API 密钥未设置" >&2
    exit 1
fi

# 2. 调用 API
RESPONSE=$(curl -s --request GET \
    --url "https://api.tavily.com/usage" \
    --header "Authorization: Bearer $API_KEY")

# 3. 解析并输出
python3 -c "
import json, sys
data = json.loads('''$RESPONSE''')

key = data.get('key', {})
account = data.get('account', {})
records = []

if key:
    records.append({
        'provider': 'tavily',
        'resource': 'key',
        'type': 'quota_basic',
        'limit': key.get('limit'),
        'usage': key.get('usage'),
        'unit': 'requests',
    })
if account.get('plan_limit'):
    records.append({
        'provider': 'tavily',
        'resource': 'account_plan',
        'type': 'quota_basic',
        'limit': account.get('plan_limit'),
        'usage': account.get('plan_usage'),
        'unit': 'requests',
    })

json.dump(records, sys.stdout)
"
```

### Python 版

参见 `scripts/tavily.py.example`。

### Node.js 版

```javascript
#!/usr/bin/env node
const config = JSON.parse(process.env.BALANCEHUB_CONFIG);
const apiKey = process.env[config.api_key_env];

if (!apiKey) {
    console.error("API 密钥未设置");
    process.exit(1);
}

fetch("https://api.tavily.com/usage", {
    headers: { Authorization: `Bearer ${apiKey}` }
})
    .then(r => r.json())
    .then(data => {
        const records = [];
        if (data.key) {
            records.push({
                provider: "tavily",
                resource: "key",
                type: "quota_basic",
                limit: data.key.limit,
                usage: data.key.usage,
                unit: "requests",
            });
        }
        // ... 更多记录
        console.log(JSON.stringify(records));
    })
    .catch(err => {
        console.error(err.message);
        process.exit(1);
    });
```

## 结果类型

脚本可以返回两种类型的记录。

### quota_basic — 配额使用量

| 字段 | 类型 | 必须 | 说明 |
|---|---|---|---|
| `provider` | string | 是 | provider ID |
| `resource` | string | 是 | 资源 ID（如 `"key"`、`"account_plan"`） |
| `type` | string | 是 | 固定为 `"quota_basic"` |
| `limit` | number or null | 否 | 配额上限 |
| `usage` | number or null | 否 | 已使用量 |
| `unit` | string or null | 否 | 单位 |

> `usage_pct`、`fetched_at`、`cached` 字段由 BalanceHub 主程序自动填充，
> 脚本中不需要输出这些字段。

### balance_basic — 余额

| 字段 | 类型 | 必须 | 说明 |
|---|---|---|---|
| `provider` | string | 是 | provider ID |
| `resource` | string | 是 | 资源 ID |
| `type` | string | 是 | 固定为 `"balance_basic"` |
| `balance` | number or null | 否 | 余额数值 |
| `unit` | string or null | 否 | 货币单位 |

### 完整示例

一个 provider 可以一次返回多条记录。例如 Tavily 可以同时返回：

```json
[
  { "provider": "tavily", "resource": "key", "type": "quota_basic",
    "limit": 1000, "usage": 150, "unit": "requests" },
  { "provider": "tavily", "resource": "account_plan", "type": "quota_basic",
    "limit": 15000, "usage": 500, "unit": "requests" },
  { "provider": "tavily", "resource": "paygo", "type": "quota_basic",
    "limit": 100, "usage": 25, "unit": "requests" }
]
```

## 从 curl 到脚本的映射

| curl 参数 | 脚本中的等价写法 |
|---|---|
| `--request GET` | `curl -s`、`requests.get()`、`fetch(url)` |
| `--header 'Authorization: Bearer <token>'` | `-H "Authorization: Bearer $TOKEN"` |
| `--data '{"key":"val"}'` | `-d '{"key":"val"}' -H "Content-Type: application/json"` |
| `--url <URL>` | URL 直接作为参数 |
| 响应 JSON | `curl ... | jq` / `response.json()` / `json.load(resp)` |

## 配置文件

```toml
[providers.tavily]
enabled = true
type = "tavily"
script = "./scripts/tavily.sh"    # ← 脚本路径（可以是任何语言）
api_key_env = "TAVILY_API_KEY"     # ← 脚本自定义配置（通过 BALANCEHUB_CONFIG 传递）
```

- `script` — 脚本路径，相对于配置文件目录（必须）
- 其余字段由 provider 自定义，通过 `BALANCEHUB_CONFIG` 传递给脚本

## 调试脚本

在独立调试脚本时，可以手动设置环境变量：

```bash
export BALANCEHUB_CONFIG='{"api_key_env":"TAVILY_API_KEY"}'
export TAVILY_API_KEY="sk-..."
./scripts/tavily.sh
```

如果脚本输出正确，你应该能在终端看到 JSON 数组。

然后在 BalanceHub 中验证：

```bash
dotnet run --project src/BalanceHub.Cli -- doctor
dotnet run --project src/BalanceHub.Cli -- get tavily --pretty
```

## 注意事项

1. **权限**：脚本文件需要有执行权限（`chmod +x`）
2. **路径**：`script` 配置使用相对路径时，基于配置文件所在目录解析
3. **工作目录**：脚本启动时工作目录设为脚本所在目录
4. **超时**：BalanceHub 对脚本执行没有硬超时限制，建议脚本自己处理超时
5. **API 密钥**：始终通过环境变量传递，不要硬编码在脚本或配置文件中
6. **错误消息**：失败时输出的错误消息会显示给用户，建议提供有意义的说明
7. **输出格式**：确保 JSON 输出是有效的 JSON 数组，否则 BalanceHub 会报错
