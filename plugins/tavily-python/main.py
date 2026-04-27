#!/usr/bin/env python3
"""
Tavily 配额查询插件 — Python 版

安装依赖:
  pip install -r plugins/tavily-python/requirements.txt

配置 balancehub.toml:
  [providers.tavily-python]
  enabled = true
  type = "tavily"
  plugin = "./plugins/tavily-python"
  api_key = "sk-tavily-xxxxx"
"""

import json
import os
import sys
import requests


def main():
    # 1. 读取 BALANCEHUB_CONFIG
    config_raw = os.environ.get("BALANCEHUB_CONFIG")
    if not config_raw:
        print("错误: BALANCEHUB_CONFIG 环境变量未设置", file=sys.stderr)
        sys.exit(1)

    config = json.loads(config_raw)
    api_key = config.get("api_key")
    if not api_key:
        print("错误: 配置缺少 api_key", file=sys.stderr)
        sys.exit(1)

    # 2. 调用 Tavily API
    url = "https://api.tavily.com/usage"
    headers = {"Authorization": f"Bearer {api_key}"}

    try:
        resp = requests.get(url, headers=headers, timeout=15)
        resp.raise_for_status()
        data = resp.json()
    except Exception as e:
        print(f"错误: API 请求失败: {e}", file=sys.stderr)
        sys.exit(1)

    # 3. 转换为标准 ProviderRecord 格式
    records = []

    key = data.get("key", {})
    if key:
        records.append({
            "provider": "tavily",
            "resource": "key",
            "type": "quota_basic",
            "limit": key.get("limit"),
            "usage": key.get("usage"),
            "unit": "requests",
        })

    account = data.get("account", {})
    if account:
        if account.get("plan_limit"):
            records.append({
                "provider": "tavily",
                "resource": "account_plan",
                "type": "quota_basic",
                "limit": account.get("plan_limit"),
                "usage": account.get("plan_usage"),
                "unit": "requests",
            })
        if account.get("paygo_limit"):
            records.append({
                "provider": "tavily",
                "resource": "paygo",
                "type": "quota_basic",
                "limit": account.get("paygo_limit"),
                "usage": account.get("paygo_usage"),
                "unit": "requests",
            })

    # 4. 输出 JSON 数组
    json.dump(records, sys.stdout, indent=2)


if __name__ == "__main__":
    main()
