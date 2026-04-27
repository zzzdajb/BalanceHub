# BalanceHub 插件开发指南

## 概述

BalanceHub 的插件机制建立在 **外部脚本** 之上。任何人都可以用自己熟悉的语言（Bash、Python、Node.js、PowerShell 等）编写 provider 脚本，只要遵守输入输出合约即可。**你不需要会 C#**。

## 插件目录约定

每个插件独立放在 `plugins/<name>/` 目录下，实现与其它插件的隔离：

```
plugins/
  tavily-python/
    main.py
    requirements.txt
  tavily-bash/
    main.sh
  deepseek-api-bash/
    main.sh
```

BalanceHub 会自动在插件目录中查找 `main.py` → `main.sh` → `main.js` 作为入口脚本。配置时只需指定目录路径：

```toml
[providers.tavily-python]
enabled = true
type = "tavily"
plugin = "./plugins/tavily-python"
api_key = "sk-tavily-xxxxx"
```

如果不想遵循 `main.py` 约定，也可以用 `script` 字段直接指定脚本文件路径：

```toml
[providers.tavily-bash]
enabled = true
type = "tavily"
script = "./plugins/tavily-bash/main.sh"
api_key = "sk-tavily-xxxxx"
```

## 插件合约

```
┌─────────────────┐     环境变量 BALANCEHUB_CONFIG     ┌────────────────┐
│                 │ ──────────────────────────────────► │                │
│   BalanceHub    │      stdout: JSON 数组              │  Provider 脚本 │
│   (主程序)       │ ◄────────────────────────────────── │  (任意语言)     │
│                 │     退出码: 0 成功 / 非0 失败        │                │
└─────────────────┘                                     └────────────────┘
```

### 输入：BALANCEHUB_CONFIG

BalanceHub 将 TOML 配置中该 provider 节的所有键值对序列化为 JSON，通过环境变量 `BALANCEHUB_CONFIG` 传递。

```toml
[providers.tavily-python]
enabled = true
type = "tavily"
plugin = "./plugins/tavily-python"
api_key = "sk-tavily-xxxxx"
```

脚本收到的环境变量值：

```json
{ "enabled": true, "type": "tavily", "plugin": "./plugins/tavily-python", "api_key": "sk-tavily-xxxxx" }
```

API 密钥直接写在 `api_key` 字段中。`balancehub.toml` 已被 .gitignore 排除，不会提交到仓库。

### 输出：stdout JSON 数组

脚本向 stdout 输出 JSON 数组，每条记录包含 `type`、`provider`、`resource` 三个必填字段：

```json
[
  { "type": "quota_basic", "provider": "tavily", "resource": "key", "limit": 1000, "usage": 150, "unit": "requests" },
  { "type": "balance_basic", "provider": "tavily", "resource": "account", "balance": 5.0, "unit": "USD" }
]
```

> `usage_pct`、`fetched_at`、`cached` 由 BalanceHub 主程序自动填充，脚本不需要输出这些字段。

### 退出码

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 非 0 | 失败（stderr 内容作为错误消息上报） |

### stderr

退出码为 0 时 BalanceHub 忽略 stderr；退出码非 0 时 stderr 内容作为错误信息上报。可用于输出调试日志。

## 合约速查表

| 项目 | 要求 |
|---|---|
| 输入 | `BALANCEHUB_CONFIG` 环境变量（JSON 字符串） |
| 输出 | stdout — JSON 数组，元素必须包含 `type`、`provider`、`resource` |
| 退出码 | 0 成功，非 0 失败（stderr 为错误消息） |
| 入口约定 | 插件目录下的 `main.py` / `main.sh` / `main.js`（可用 `script` 覆盖） |
| 依赖 | 脚本所需运行时需预装（bash、python3、node 等） |
| 权限 | 需要执行权限（`chmod +x`） |

## 结果类型

### quota_basic — 配额使用量

| 字段 | 类型 | 必须 | 说明 |
|---|---|---|---|
| `type` | string | 是 | 固定为 `"quota_basic"` |
| `provider` | string | 是 | provider ID |
| `resource` | string | 是 | 资源 ID |
| `limit` | number or null | 否 | 配额上限 |
| `usage` | number or null | 否 | 已使用量 |
| `unit` | string or null | 否 | 单位 |

### balance_basic — 余额

| 字段 | 类型 | 必须 | 说明 |
|---|---|---|---|
| `type` | string | 是 | 固定为 `"balance_basic"` |
| `provider` | string | 是 | provider ID |
| `resource` | string | 是 | 资源 ID |
| `balance` | number or null | 否 | 余额数值 |
| `unit` | string or null | 否 | 货币单位 |

## 快速开始

### Python 版（需 requests 库）

官方示例见 `plugins/tavily-python/`：

```bash
pip install -r plugins/tavily-python/requirements.txt
chmod +x plugins/tavily-python/main.py
```

核心流程：

```python
#!/usr/bin/env python3
import json, os, sys, requests

config = json.loads(os.environ["BALANCEHUB_CONFIG"])
api_key = config.get("api_key")

resp = requests.get("https://api.tavily.com/usage",
    headers={"Authorization": f"Bearer {api_key}"}, timeout=15)
resp.raise_for_status()
data = resp.json()

records = []
key = data.get("key", {})
if key:
    records.append({"provider": "tavily", "resource": "key",
        "type": "quota_basic", "limit": key.get("limit"),
        "usage": key.get("usage"), "unit": "requests"})

json.dump(records, sys.stdout)
```

### Bash 版（无需 Python，需 curl）

完整示例见 `plugins/tavily-bash/main.sh`、`plugins/deepseek-api-bash/main.sh`：

```bash
#!/usr/bin/env bash
set -euo pipefail

API_KEY=$(echo "$BALANCEHUB_CONFIG" | python3 -c "import sys,json; print(json.load(sys.stdin).get('api_key',''))")
[ -z "$API_KEY" ] && { echo "错误: 配置中缺少 api_key" >&2; exit 1; }

RESPONSE=$(curl -s --request GET --url "https://api.tavily.com/usage" --header "Authorization: Bearer $API_KEY")

python3 -c "
import json, sys
data = json.loads('''$RESPONSE''')
records = []
key = data.get('key', {})
if key:
    records.append({'provider':'tavily','resource':'key','type':'quota_basic','limit':key.get('limit'),'usage':key.get('usage'),'unit':'requests'})
json.dump(records, sys.stdout)
"
```

## 调试脚本

```bash
export BALANCEHUB_CONFIG='{"api_key":"sk-tavily-xxxxx"}'
# Python 版
./plugins/tavily-python/main.py
# Bash 版
./plugins/tavily-bash/main.sh

# 在 BalanceHub 中验证
dotnet run --project src/BalanceHub.Cli -- doctor
dotnet run --project src/BalanceHub.Cli -- get tavily-python --pretty
dotnet run --project src/BalanceHub.Cli -- get tavily-bash --pretty
```

## 注意事项

1. **权限**：`chmod +x` 入口脚本
2. **路径**：`plugin` 和 `script` 的相对路径基于配置文件所在目录解析
3. **工作目录**：脚本启动时工作目录设为脚本所在目录
4. **超时**：建议脚本自己处理超时
5. **API 密钥**：直接填写在配置文件的 `api_key` 字段中（`balancehub.toml` 已被 .gitignore 排除）
6. **错误消息**：失败时输出有意义的错误说明
7. **输出格式**：确保 JSON 是合法的数组格式
