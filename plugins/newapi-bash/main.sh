#!/usr/bin/env bash
# NewAPI 余额查询插件 — 纯 Bash 版（零外部依赖，仅需 curl）
#
# 通过 NewAPI 的 /api/user/ 接口获取用户剩余额度，
# 除以 500,000 转换为 USD。
#
# 配置 balancehub.toml:
#   [providers.newapi-bash]
#   enabled = true
#   type = "newapi"
#   plugin = "./plugins/newapi-bash"
#   api_key = "你的API访问令牌"
#   base_url = "https://newapi.example.com"
#   user_id = "1"

set -euo pipefail

# ---- 1. 从 BALANCEHUB_CONFIG 读取配置（纯 grep+sed）----
API_KEY=$(echo "$BALANCEHUB_CONFIG" | sed -n 's/.*"api_key"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
BASE_URL=$(echo "$BALANCEHUB_CONFIG" | sed -n 's/.*"base_url"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | sed 's:/*$::')
USER_ID=$(echo "$BALANCEHUB_CONFIG" | sed -n 's/.*"user_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
[ -z "$USER_ID" ] && USER_ID="1"
# 读取 TOML section 名称，用于区分同名 type 的多个实例
PROVIDER_ID=$(echo "$BALANCEHUB_CONFIG" | sed -n 's/.*"provider_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
[ -z "$PROVIDER_ID" ] && PROVIDER_ID="newapi"

[ -z "$API_KEY" ] && { echo "错误: 配置中缺少 api_key" >&2; exit 1; }
[ -z "$BASE_URL" ] && { echo "错误: 配置中缺少 base_url" >&2; exit 1; }

# ---- 2. 调用 NewAPI 用户信息接口 ----
# 同时发送多个兼容性 user-id header，适配各种 One-API fork
RESPONSE=$(curl -s --request GET \
    "${BASE_URL}/api/user/self" \
    --header "Authorization: Bearer ${API_KEY}" \
    --header "New-Api-User: ${USER_ID}" \
    --header "User-id: ${USER_ID}" \
    --header "neo-api-user: ${USER_ID}" \
    --header "Content-Type: application/json") || {
    echo "错误: NewAPI 请求失败 (${BASE_URL}/api/user/self)" >&2
    exit 1
}

[ -z "$RESPONSE" ] && { echo "错误: API 返回为空" >&2; exit 1; }

# ---- 3. 解析响应 & 计算 USD（全整数算术，无浮点）----
echo "$RESPONSE" | grep -q '"success"[[:space:]]*:[[:space:]]*true' || {
    echo "错误: API 返回失败" >&2
    exit 1
}

# 提取 raw quota（含负号），0 兜底
RAW_QUOTA=$(echo "$RESPONSE" | sed -n 's/.*"quota"[[:space:]]*:[[:space:]]*\(-\{0,1\}[0-9]*\).*/\1/p' | head -1)
[ -z "$RAW_QUOTA" ] && RAW_QUOTA="0"
QUOTA=$RAW_QUOTA

# 纯整数运算：quota * 100 / 500000，支持负数
SIGN=1
[ "${QUOTA:0:1}" = "-" ] && { SIGN=-1; QUOTA="${QUOTA:1}"; }
VAL=$(( SIGN * QUOTA * 100 / 500000 ))
INT_PART=$(( VAL / 100 ))
DEC_PART=$(( VAL % 100 ))
[ $DEC_PART -lt 0 ] && DEC_PART=$(( -DEC_PART ))
printf -v DEC_FMT "%02d" "$DEC_PART"

API_USER_ID=$(echo "$RESPONSE" | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p' | head -1)
[ -z "$API_USER_ID" ] && API_USER_ID="$USER_ID"

# ---- 4. 输出标准 JSON 数组（纯 bash echo）----
cat <<EOF
[
  {
    "provider": "$PROVIDER_ID",
    "resource": "user_${API_USER_ID}",
    "type": "balance_basic",
    "balance": ${INT_PART}.${DEC_FMT},
    "unit": "USD"
  }
]
EOF
