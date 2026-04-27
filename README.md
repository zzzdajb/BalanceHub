# BalanceHub

查询第三方服务配额和余额的 CLI 工具。

> **如果你是 AI/Agent**，请切换到 [README.agent.md](README.agent.md) 阅读，那里包含更详细的用法说明。

## 概述

BalanceHub 是一个命令行工具，用于从不同第三方服务查询配额使用量和余额信息。
它的设计目标是提供稳定、机器友好的 JSON 输出，便于直接接入 LLM、脚本和自动化工具。

当前支持的 provider 类型：

- `mock` — 模拟 provider，用于测试和演示

## 快速开始

```bash
# 1. 复制配置文件模板
cp balancehub.toml.example balancehub.toml

# 2. 构建
dotnet build

# 3. 查看所有已配置的 provider
dotnet run --project src/BalanceHub.Cli -- list

# 4. 查询所有 provider
dotnet run --project src/BalanceHub.Cli -- get --pretty

# 5. 诊断检查
dotnet run --project src/BalanceHub.Cli -- doctor
```

> 后续可编译为独立可执行文件：`dotnet publish src/BalanceHub.Cli -c Release -o ./publish`

## 命令

| 命令 | 说明 |
|---|---|
| `balancehub get` | 查询所有已启用 provider 的配额/余额 |
| `balancehub get <provider>` | 查询指定 provider |
| `balancehub list` | 列出已配置的 provider |
| `balancehub doctor` | 检查配置和 provider 就绪状态 |

## 选项

| 选项 | 适用命令 | 说明 |
|---|---|---|
| `--refresh` | get | 强制刷新，跳过缓存 |
| `--no-cache` | get | 完全禁用缓存（不读不写） |
| `--config <path>` | get, list, doctor | 自定义配置文件路径 |
| `--pretty` | get | 美化 JSON 输出（带缩进） |

## 返回格式

所有返回结构化数据的命令使用统一的 JSON 响应信封：

```json
{
  "ok": true,
  "data": [],
  "errors": []
}
```

- `ok` — 完全成功时为 `true`；有任何 provider 失败时为 `false`
- `data` — 成功的 provider 结果列表（可能为空）
- `errors` — 错误列表（可能为空）

### 结果类型

**quota_basic** — 配额使用量（已使用量相对固定上限）：

```json
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
```

**balance_basic** — 余额或积分：

```json
{
  "type": "balance_basic",
  "provider": "perplexity",
  "resource": "account",
  "balance": 5.0,
  "unit": "USD",
  "fetched_at": "2026-04-27T12:00:00Z",
  "cached": false
}
```

## 配置

配置文件为 TOML 格式，默认路径为 `./balancehub.toml`。

```toml
[cache]
enabled = true
ttl_seconds = 300
directory = ".balancehub/cache"

[providers.mock]
enabled = true
type = "mock"
```

> API 密钥可直接写入配置文件（`balancehub.toml` 已被 .gitignore 排除，不会提交到仓库）。

## 退出码

| 退出码 | 含义 |
|---|---|
| 0 | 全部成功 |
| 1 | 部分或全部 provider 失败 |
| 2 | 无效的命令行参数 |
| 3 | 配置错误 |
| 4 | 未预期错误 |

## 项目结构

```
BalanceHub/
├── src/
│   ├── BalanceHub.Core/      核心模型和接口
│   ├── BalanceHub.Providers/ 内置 provider 实现
│   └── BalanceHub.Cli/       命令行入口和服务
└── tests/                    单元测试
```

## 技术栈

- .NET 10 / C#
- Tomlyn（TOML 解析）
- xUnit（测试）

## 致谢
感谢 [Linux.do](https://linux.do/) 社区传播支持。

感谢 **Claude Code** 提供的工具支持。

感谢 **DeepSeek-V4-Flash** 和 **GPT5.5** 提供的模型支持。
