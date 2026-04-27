#!/usr/bin/env bash
# Tavily 配额查询插件 — Bash 版
# 无需 Python，依赖 curl 即可。
#
# 配置 balancehub.toml:
#   [providers.tavily]
#   enabled = true
#   type = "tavily"
#   plugin = "./plugins/tavily-bash"
#   api_key = "sk-tavily-xxxxx"

set -euo pipefail

# ---- 1. 从 BALANCEHUB_CONFIG 读取 API 密钥 ----
if command -v python3 &>/dev/null; then
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | python3 -c "import sys,json; print(json.load(sys.stdin).get('api_key',''))")
elif command -v jq &>/dev/null; then
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | jq -r '.api_key // ""')
else
    # 最后的回退：用 grep+sed 提取 api_key 的值
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | grep -o '"api_key":"[^"]*"' | cut -d'"' -f4)
fi

if [ -z "$API_KEY" ]; then
    echo "错误: 配置中缺少 api_key" >&2
    exit 1
fi

# ---- 2. 调用 Tavily API ----
RESPONSE=$(curl -s --request GET \
    --url "https://api.tavily.com/usage" \
    --header "Authorization: Bearer $API_KEY")

if [ $? -ne 0 ] || [ -z "$RESPONSE" ]; then
    echo "错误: Tavily API 请求失败" >&2
    exit 1
fi

# ---- 3. 解析响应并输出标准格式 ----
# 优先用 python3，其次 jq，最后 grep 回退
if command -v python3 &>/dev/null; then
    python3 -c "
import json, sys

try:
    data = json.loads('''$RESPONSE''')
except json.JSONDecodeError as e:
    print(f'JSON 解析失败: {e}', file=sys.stderr)
    sys.exit(1)

records = []

key = data.get('key', {})
if key:
    records.append({
        'provider': 'tavily',
        'resource': 'key',
        'type': 'quota_basic',
        'limit': key.get('limit'),
        'usage': key.get('usage'),
        'unit': 'requests',
    })

account = data.get('account', {})
if account:
    if account.get('plan_limit'):
        records.append({
            'provider': 'tavily',
            'resource': 'account_plan',
            'type': 'quota_basic',
            'limit': account.get('plan_limit'),
            'usage': account.get('plan_usage'),
            'unit': 'requests',
        })
    if account.get('paygo_limit'):
        records.append({
            'provider': 'tavily',
            'resource': 'paygo',
            'type': 'quota_basic',
            'limit': account.get('paygo_limit'),
            'usage': account.get('paygo_usage'),
            'unit': 'requests',
        })

json.dump(records, sys.stdout, indent=2)
" || { echo "错误: 响应解析失败" >&2; exit 1; }
elif command -v jq &>/dev/null; then
    # 纯 jq 版本，处理 key 和 account 层面的配额
    echo "$RESPONSE" | jq -c '
        [.key | {provider:"tavily", resource:"key", type:"quota_basic", limit:.limit, usage:.usage, unit:"requests"}]
        + [.account | select(.plan_limit) | {provider:"tavily", resource:"account_plan", type:"quota_basic", limit:.plan_limit, usage:.plan_usage, unit:"requests"}]
        + [.account | select(.paygo_limit) | {provider:"tavily", resource:"paygo", type:"quota_basic", limit:.paygo_limit, usage:.paygo_usage, unit:"requests"}]
    ' 2>/dev/null || { echo "错误: jq 解析失败" >&2; exit 1; }
else
    echo "错误: 需要 python3 或 jq 来解析 JSON" >&2
    exit 1
fi
