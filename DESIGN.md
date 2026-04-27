# BalanceHub Design Document

## 1. Overview

BalanceHub is a CLI-only tool for querying quota and balance information from different third-party services.

The primary users are LLMs, agents, shell scripts, and developers. The main design goal is to provide stable, machine-friendly JSON output that can be called directly from Bash, PowerShell, or other automation environments.

Examples of target services:

- Tavily quota usage
- Perplexity balance or credit
- Other API providers with quota, usage, balance, or credit information

BalanceHub itself does not know how every service exposes its quota. Each provider implementation is responsible for fetching and normalizing data from one service.

## 2. Goals

- Provide a simple CLI for agents and scripts.
- Default to JSON output.
- Support querying all enabled providers with one command.
- Support querying a single provider by ID.
- Normalize provider data into a small set of stable result types.
- Use a human-friendly and agent-friendly TOML configuration file.
- Support caching to avoid unnecessary provider requests.
- Always show when the returned data was actually fetched.
- Keep the code beginner-friendly and easy to extend.

## 3. Non-Goals

BalanceHub should not implement these features in the initial design:

- MCP server
- HTTP API server
- Web UI
- Background daemon
- Complex plugin marketplace
- Complex authentication or secret-management system
- Database-backed persistence

The CLI is the product interface. Other integration styles are intentionally out of scope.

## 4. Product Name and Command Name

Product name:

```text
BalanceHub
```

CLI command name:

```text
balancehub
```

The command name should stay lowercase because it is easier to type and easier for agents to use in shell commands.

## 5. CLI Design

The CLI should use short, predictable commands.

### 5.1 Commands

Query all enabled providers:

```bash
balancehub get
```

Query one provider:

```bash
balancehub get tavily
```

List configured providers:

```bash
balancehub list
```

Check configuration and provider readiness:

```bash
balancehub doctor
```

### 5.2 Common Options

Force live refresh and bypass cache:

```bash
balancehub get tavily --refresh
```

Disable both reading and writing cache:

```bash
balancehub get tavily --no-cache
```

Use a custom config file:

```bash
balancehub get --config ./balancehub.toml
```

Pretty-print JSON:

```bash
balancehub get --pretty
```

### 5.3 Output Format

JSON is the default output format.

Human-readable text output is not required for the MVP. If added later, it should be optional and must not change the default JSON behavior.

## 6. Result Types

The first version only supports two result types:

- `quota_basic`
- `balance_basic`

More types can be added later, but the first version should stay intentionally small.

## 7. JSON Response Envelope

Every command that returns structured data should use the same top-level response envelope.

Successful response:

```json
{
  "ok": true,
  "data": [],
  "errors": []
}
```

Partial or failed response:

```json
{
  "ok": false,
  "data": [],
  "errors": []
}
```

Rules:

- `ok` is `true` only when the command fully succeeds.
- `ok` is `false` if any requested provider fails.
- `data` may still contain successful provider results when `ok` is `false`.
- `errors` should be an empty array when there are no errors.

This makes the output easy for agents to parse and safe for partial results.

## 8. quota_basic Schema

`quota_basic` represents usage against a fixed limit.

Required fields:

```json
{
  "provider": "tavily",
  "resource": "key",
  "type": "quota_basic",
  "limit": 1000,
  "usage": 150,
  "usage_pct": 15.0,
  "unit": "requests",
  "fetched_at": "2026-04-27T12:00:00Z",
  "cached": false
}
```

Field meanings:

- `provider`: lowercase provider ID, such as `tavily`.
- `resource`: lowercase resource ID within the provider, such as `key` or `account_plan`.
- `type`: always `quota_basic`.
- `limit`: total allowed quota.
- `usage`: used quota.
- `usage_pct`: automatically calculated by BalanceHub.
- `unit`: quota unit, such as `requests`; may be `null`.
- `fetched_at`: when the provider data was actually fetched.
- `cached`: whether the current CLI result came from cache.

### 8.1 usage_pct Calculation

BalanceHub calculates `usage_pct`.

Formula:

```text
usage_pct = usage / limit * 100
```

Rules:

- Round to 2 decimal places.
- If `limit` is `0`, negative, or unknown, return `null`.
- Providers should not calculate `usage_pct` themselves.

### 8.2 Multiple Quotas Per Provider

A provider may return multiple `quota_basic` items.

For example, Tavily may expose both key-level usage and account-level plan usage:

```json
{
  "ok": true,
  "data": [
    {
      "provider": "tavily",
      "resource": "key",
      "type": "quota_basic",
      "limit": 1000,
      "usage": 150,
      "usage_pct": 15.0,
      "unit": "requests",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": false
    },
    {
      "provider": "tavily",
      "resource": "account_plan",
      "type": "quota_basic",
      "limit": 15000,
      "usage": 500,
      "usage_pct": 3.33,
      "unit": "requests",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": false
    }
  ],
  "errors": []
}
```

## 9. balance_basic Schema

`balance_basic` represents a simple remaining balance or credit value.

Example:

```json
{
  "provider": "perplexity",
  "resource": "account",
  "type": "balance_basic",
  "balance": 5.0,
  "unit": "USD",
  "fetched_at": "2026-04-27T12:00:00Z",
  "cached": false
}
```

Field meanings:

- `provider`: lowercase provider ID.
- `resource`: lowercase resource ID within the provider.
- `type`: always `balance_basic`.
- `balance`: current balance value.
- `unit`: balance unit, such as `USD`; may be `null`.
- `fetched_at`: when the provider data was actually fetched.
- `cached`: whether the current CLI result came from cache.

## 10. Provider IDs and Resource IDs

Provider IDs should be lowercase and stable.

Examples:

```text
tavily
perplexity
openai
anthropic
```

Resource IDs should also be lowercase and stable.

Examples:

```text
key
account
account_plan
paygo
```

Use `snake_case` for multi-word IDs.

## 11. TOML Configuration

The default config file should be:

```text
./balancehub.toml
```

A custom config file can be passed with:

```bash
balancehub get --config ./path/to/balancehub.toml
```

Example config:

```toml
[cache]
enabled = true
ttl_seconds = 300
directory = ".balancehub/cache"

[providers.tavily]
enabled = true
type = "tavily"
api_key_env = "TAVILY_API_KEY"

[providers.perplexity]
enabled = true
type = "perplexity"
cookie_file = "./secrets/perplexity.cookie"
```

Rules:

- TOML is the main configuration format.
- Secrets should not be stored directly in the config file when avoidable.
- API keys should usually be referenced through environment variables.
- Cookie values should usually be stored in separate files and referenced by path.
- Relative paths should be resolved relative to the config file location.

## 12. Cache Design

Caching should be enabled by default.

The cache prevents agents from repeatedly triggering slow or expensive provider requests.

### 12.1 Cache Fields

Every returned item must include:

```json
{
  "fetched_at": "2026-04-27T12:00:00Z",
  "cached": true
}
```

Meanings:

- `fetched_at`: when the data was originally fetched from the provider.
- `cached`: whether this CLI invocation returned cached data.

This lets an agent decide whether the data is fresh enough.

### 12.2 Cache Behavior

Default behavior:

- If cache is enabled and not expired, return cached data.
- If cache is missing or expired, fetch fresh data.
- When fresh data is fetched successfully, write it to cache.

`--refresh` behavior:

- Ignore existing cache.
- Fetch fresh data.
- Write fresh result to cache if cache is enabled.

`--no-cache` behavior:

- Do not read cache.
- Do not write cache.
- Always fetch fresh data.

### 12.3 Cache Storage

The default cache directory should be:

```text
.balancehub/cache
```

The cache directory can be configured:

```toml
[cache]
directory = ".balancehub/cache"
```

For the MVP, a simple JSON file cache is enough. A database is not needed.

## 13. Error Handling

Errors should be structured and predictable.

Example:

```json
{
  "ok": false,
  "data": [],
  "errors": [
    {
      "provider": "tavily",
      "code": "missing_config",
      "message": "Missing environment variable: TAVILY_API_KEY"
    }
  ]
}
```

### 13.1 Error Object

Error fields:

```json
{
  "provider": "tavily",
  "code": "provider_failed",
  "message": "Tavily quota request failed."
}
```

Field meanings:

- `provider`: provider ID if the error is provider-specific; otherwise `null`.
- `code`: stable machine-readable error code.
- `message`: short human-readable explanation.

### 13.2 Recommended Error Codes

```text
invalid_args
config_not_found
invalid_config
provider_not_found
provider_disabled
missing_config
auth_failed
network_failed
parse_failed
provider_failed
cache_failed
unexpected_error
```

### 13.3 Partial Failure

If querying all providers and only some fail, BalanceHub should return successful data and errors together.

Example:

```json
{
  "ok": false,
  "data": [
    {
      "provider": "perplexity",
      "resource": "account",
      "type": "balance_basic",
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

## 14. Exit Codes

Recommended exit codes:

```text
0  Full success
1  Provider failure or partial failure
2  Invalid CLI arguments
3  Configuration error
4  Unexpected error
```

JSON output should still be written when possible, even for non-zero exit codes.

## 15. Provider Model

Provider implementations are responsible for fetching provider-specific data and returning normalized records.

Examples:

- Tavily provider calls an official quota endpoint.
- Perplexity provider may use cookies or another available mechanism.
- A future provider may scrape a web page if no official API exists.

The main CLI should not contain provider-specific parsing logic.

### 15.1 Provider Responsibilities

A provider should:

- Read its own configuration.
- Fetch quota or balance data from the external service.
- Convert external service data into BalanceHub result types.
- Return clear provider-specific errors.

A provider should not:

- Format CLI output.
- Decide cache behavior.
- Calculate `usage_pct`.
- Read unrelated provider configuration.

### 15.2 Main Program Responsibilities

The main CLI should:

- Parse CLI arguments.
- Load TOML configuration.
- Select requested providers.
- Handle cache read/write.
- Call providers.
- Calculate `usage_pct`.
- Add `cached` and `fetched_at` metadata.
- Build the final JSON response envelope.
- Set the correct exit code.

## 16. Suggested C# Project Structure

The first implementation can stay simple.

Recommended structure:

```text
BalanceHub/
├─ DESIGN.md
├─ balancehub.toml.example
├─ src/
│  ├─ BalanceHub.Cli/
│  ├─ BalanceHub.Core/
│  └─ BalanceHub.Providers/
└─ tests/
   ├─ BalanceHub.Core.Tests/
   └─ BalanceHub.Cli.Tests/
```

Project purposes:

- `BalanceHub.Cli`: command parsing, config loading, orchestration, output.
- `BalanceHub.Core`: shared models, result types, errors, provider interfaces.
- `BalanceHub.Providers`: built-in provider implementations.
- `tests`: focused tests for schema, config, cache, and command behavior.

For the MVP, built-in providers are enough. Dynamic DLL plugin loading is not required.

## 17. Beginner-Friendly Implementation Rules

The code should favor clarity over cleverness.

Recommended rules:

- Use explicit model classes instead of loosely typed dictionaries.
- Keep provider classes small.
- Keep CLI commands short and direct.
- Avoid reflection-heavy plugin loading in the MVP.
- Avoid background services.
- Avoid databases.
- Use clear error objects instead of throwing raw exceptions to the top level.
- Add tests for JSON shape and cache behavior.

## 18. MVP Scope

The MVP should include:

- `balancehub get`
- `balancehub get <provider>`
- `balancehub list`
- `balancehub doctor`
- Default JSON output
- TOML config loading
- Cache support
- `quota_basic`
- `balance_basic`
- At least one mock provider for testing
- Optional Tavily provider if its quota API is available and stable

The MVP should not include:

- MCP
- HTTP
- Web UI
- Dynamic plugin loading
- Complex provider marketplace
- Complex secret storage

## 19. Example Full Output

Example command:

```bash
balancehub get
```

Example output:

```json
{
  "ok": true,
  "data": [
    {
      "provider": "tavily",
      "resource": "key",
      "type": "quota_basic",
      "limit": 1000,
      "usage": 150,
      "usage_pct": 15.0,
      "unit": "requests",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": false
    },
    {
      "provider": "perplexity",
      "resource": "account",
      "type": "balance_basic",
      "balance": 5.0,
      "unit": "USD",
      "fetched_at": "2026-04-27T12:00:00Z",
      "cached": true
    }
  ],
  "errors": []
}
```

## 20. Implementation Notes for Codex

When implementing this design:

1. Build the CLI around stable JSON output first.
2. Add a mock provider before adding real providers.
3. Add TOML config loading before provider-specific API calls.
4. Add cache behavior before expensive real provider calls.
5. Test the exact JSON field names.
6. Keep the public CLI commands stable.

The first usable version should be small, boring, and reliable.
