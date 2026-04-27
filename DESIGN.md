# BalanceHub Design Document

## 1. Overview

BalanceHub is a CLI-only tool for querying quota and balance information from different third-party services. Primary users are LLMs, agents, shell scripts, and developers. The main design goal is stable, machine-friendly JSON output.

## 2. Goals

- Simple CLI for agents and scripts, default to JSON output
- Support querying all enabled providers or a single provider by ID
- Normalize provider data into a small set of stable result types
- TOML configuration file
- Caching to avoid unnecessary provider requests
- Always show when data was actually fetched
- Keep code beginner-friendly and easy to extend

## 3. Non-Goals

No MCP server, HTTP API, Web UI, daemon, plugin marketplace, secret-management system, or database.

## 4. CLI Design

### Commands

- `balancehub get` — query all enabled providers
- `balancehub get <provider>` — query a specific provider
- `balancehub list` — list configured providers
- `balancehub doctor` — check configuration and provider readiness

### Options

| Option | Applies to | Description |
|---|---|---|
| `--refresh` | get | Force live refresh, bypass cache |
| `--no-cache` | get | Disable both reading and writing cache |
| `--config <path>` | get, list, doctor | Custom config file path |
| `--pretty` | get | Pretty-print JSON |

### Exit Codes

| Code | Meaning |
|---|---|
| 0 | Full success |
| 1 | Provider failure or partial failure |
| 2 | Invalid CLI arguments |
| 3 | Configuration error |
| 4 | Unexpected error |

## 5. JSON Response Envelope

Every command uses the same envelope:

```json
{ "ok": true, "data": [], "errors": [] }
```

- `ok` is `true` only when all providers succeed
- `data` may contain successful results even when `ok` is `false`
- `errors` is an array of error objects

### Error Object

```json
{ "provider": "tavily", "code": "provider_failed", "message": "..." }
```

## 6. Result Types

### quota_basic

Usage against a fixed limit:

| Field | Type | Description |
|---|---|---|
| `provider` | string | Provider ID |
| `resource` | string | Resource ID |
| `type` | string | Always `"quota_basic"` |
| `limit` | number or null | Total allowed quota |
| `usage` | number or null | Used quota |
| `usage_pct` | number or null | Auto-calculated: `usage / limit * 100`, rounded to 2 decimals, `null` if limit ≤ 0 |
| `unit` | string or null | e.g. `"requests"` |
| `fetched_at` | string | ISO 8601 timestamp |
| `cached` | bool | Whether result came from cache |

### balance_basic

Remaining balance or credit:

| Field | Type | Description |
|---|---|---|
| `provider` | string | Provider ID |
| `resource` | string | Resource ID |
| `type` | string | Always `"balance_basic"` |
| `balance` | number or null | Current balance |
| `unit` | string or null | e.g. `"USD"` |
| `fetched_at` | string | ISO 8601 timestamp |
| `cached` | bool | Whether result came from cache |

## 7. Configuration (TOML)

Default path: `./balancehub.toml`. Secrets should not be stored directly in config; use environment variables or external files referenced by path.

```toml
[cache]
enabled = true
ttl_seconds = 300
directory = ".balancehub/cache"

[providers.tavily]
enabled = true
type = "tavily"
script = "./scripts/tavily.sh"
api_key_env = "TAVILY_API_KEY"
```

## 8. Cache

- Default: enabled, 300s TTL, JSON file per provider in `.balancehub/cache/`
- `--refresh`: ignore existing cache, fetch fresh, write to cache
- `--no-cache`: don't read or write cache, always fetch fresh

## 9. Provider Model

Providers are external scripts. The main CLI handles: argument parsing, config loading, cache, `usage_pct` calculation, metadata (`cached`, `fetched_at`), and response assembly.

### Script Contract

| Item | Requirement |
|---|---|
| Input | `BALANCEHUB_CONFIG` env var (JSON with the provider's config section) |
| Output | stdout — JSON array of records |
| Exit code | 0 = success, non-zero = failure (stderr is error message) |
| API keys | Passed via environment variables (name configured in TOML) |

## 10. Project Structure

```
BalanceHub/
├── src/
│   ├── BalanceHub.Core/      Models, config, interfaces
│   ├── BalanceHub.Providers/ Built-in providers + ScriptProvider
│   └── BalanceHub.Cli/       Entry point, config loading, cache, orchestration
└── tests/
    ├── BalanceHub.Core.Tests/
    └── BalanceHub.Cli.Tests/
```

## 11. MVP Scope

Includes: `get`, `get <provider>`, `list`, `doctor`, JSON output, TOML config, cache, `quota_basic`, `balance_basic`, mock provider, script-based external providers.

## 12. Implementation Order

1. CLI entry point with argument parsing
2. Core models and result types
3. Mock provider
4. TOML config loading
5. Cache implementation
6. Provider orchestration
7. Script-based external provider execution
8. Tests
