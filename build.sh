#!/usr/bin/env bash
# BalanceHub 构建脚本
# 编译为单文件 Self-Contained 可执行文件，并附带 plugins 目录。
#
# 用法:
#   ./build.sh                    # 编译当前平台
#   ./build.sh linux-x64          # 编译指定平台
#   ./build.sh linux-arm64
#   ./build.sh win-x64
#   ./build.sh win-arm64
#   ./build.sh all                # 编译全部 4 个平台

set -euo pipefail

PROJECT="src/BalanceHub.Cli/BalanceHub.Cli.csproj"
OUTPUT_BASE="dist"

RIDS=()
case "${1:-}" in
    all)
        RIDS=("linux-x64" "linux-arm64" "win-x64" "win-arm64")
        ;;
    "")
        # 检测当前架构
        ARCH=$(uname -m)
        case "$ARCH" in
            x86_64)  RIDS=("linux-x64") ;;
            aarch64) RIDS=("linux-arm64") ;;
            arm64)   RIDS=("linux-arm64") ;;
            *)
                echo "未知架构: $ARCH，请明确指定 rid"
                echo "用法: $0 [linux-x64|linux-arm64|win-x64|win-arm64|all]"
                exit 1
                ;;
        esac
        ;;
    *)
        RIDS=("$1")
        ;;
esac

for RID in "${RIDS[@]}"; do
    echo "========================================"
    echo "构建: $RID"
    echo "========================================"

    OUTPUT_DIR="$OUTPUT_BASE/balancehub-$RID"

    dotnet publish "$PROJECT" \
        -c Release \
        --self-contained true \
        --runtime "$RID" \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=embedded \
        -o "$OUTPUT_DIR"

    # 复制 plugins 目录
    cp -r plugins "$OUTPUT_DIR/"

    echo "完成: $OUTPUT_DIR"
    echo ""
done

echo "全部构建完成，输出在 $OUTPUT_BASE/ 目录下"
