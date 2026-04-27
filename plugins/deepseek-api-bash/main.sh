#!/usr/bin/env bash
# DeepSeek API 余额查询插件 — Bash 版
# 依赖: curl
#
# 配置 balancehub.toml:
#   [providers.deepseek-api-bash]
#   enabled = true
#   type = "deepseek"
#   plugin = "./plugins/deepseek-api-bash"
#   api_key = "sk-xxxxx"

set -euo pipefail

# ---- 1. 从 BALANCEHUB_CONFIG 读取 API 密钥 ----
if command -v python3 &>/dev/null; then
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | python3 -c "import sys,json; print(json.load(sys.stdin).get('api_key',''))")
elif command -v jq &>/dev/null; then
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | jq -r '.api_key // ""')
else
    API_KEY=$(echo "$BALANCEHUB_CONFIG" | grep -o '"api_key":"[^"]*"' | cut -d'"' -f4)
fi

if [ -z "$API_KEY" ]; then
    echo "错误: 配置中缺少 api_key" >&2
    exit 1
fi

# ---- 2. 调用 DeepSeek API ----
RESPONSE=$(curl -s -L -X GET "https://api.deepseek.com/user/balance" \
    -H "Accept: application/json" \
    -H "Authorization: Bearer $API_KEY")

if [ $? -ne 0 ] || [ -z "$RESPONSE" ]; then
    echo "错误: DeepSeek API 请求失败" >&2
    exit 1
fi

# ---- 3. 解析响应并输出标准格式 ----
if command -v python3 &>/dev/null; then
    python3 -c "
import json, sys

try:
    data = json.loads('''$RESPONSE''')
except json.JSONDecodeError as e:
    print(f'JSON 解析失败: {e}', file=sys.stderr)
    sys.exit(1)

records = []

infos = data.get('balance_infos', [])
for info in infos:
    records.append({
        'provider': 'deepseek',
        'resource': 'account',
        'type': 'balance_basic',
        'balance': info.get('total_balance'),
        'unit': info.get('currency'),
    })

json.dump(records, sys.stdout, indent=2)
" || { echo "错误: 响应解析失败" >&2; exit 1; }
elif command -v jq &>/dev/null; then
    echo "$RESPONSE" | jq -c '
        [.balance_infos[] | {provider:"deepseek", resource:"account", type:"balance_basic", balance:.total_balance, unit:.currency}]
    ' 2>/dev/null || { echo "错误: jq 解析失败" >&2; exit 1; }
else
    echo "错误: 需要 python3 或 jq 来解析 JSON" >&2
    exit 1
fi
