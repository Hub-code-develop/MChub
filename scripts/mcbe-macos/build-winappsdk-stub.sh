#!/usr/bin/env bash
#
# 交叉编译 WinAppSDK Bootstrapper 桩(Windows x86_64),产物供 macOS 基岩版启动时
# 替换实例目录里那个会在 Wine 下失败的 Microsoft.WindowsAppRuntime.Bootstrap.dll。
#
# 源文件:scripts/mcbe-macos/winappsdk-stub/bootstrap_stub.c
# 产物:$OUT/Microsoft.WindowsAppRuntime.Bootstrap.dll
#
# 环境变量:
#   MINGW_CC  交叉编译器(默认 x86_64-w64-mingw32-gcc)
#   OUT       产物目录(默认 <repo>/artifacts/runtime/winappsdk)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${OUT:-$REPO_ROOT/artifacts/runtime/winappsdk}"
SOURCE="$REPO_ROOT/scripts/mcbe-macos/winappsdk-stub/bootstrap_stub.c"

log() { printf '\n==> %s\n' "$*"; }

MINGW_CC="${MINGW_CC:-x86_64-w64-mingw32-gcc}"
command -v "$MINGW_CC" >/dev/null 2>&1 || { echo "错误:找不到交叉编译器 $MINGW_CC" >&2; exit 1; }

log "编译器"
"$MINGW_CC" --version | head -1

log "编译 Bootstrapper 桩 -> $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"
# -static-libgcc:避免桩本身再去依赖 mingw 的运行期 DLL。
"$MINGW_CC" -O2 -shared -static-libgcc -Wall -Wextra \
  -o "$OUT/Microsoft.WindowsAppRuntime.Bootstrap.dll" "$SOURCE"

log "校验导出符号"
if command -v x86_64-w64-mingw32-objdump >/dev/null 2>&1; then
  x86_64-w64-mingw32-objdump -p "$OUT/Microsoft.WindowsAppRuntime.Bootstrap.dll" |
    grep -E 'MddBootstrapInitialize2|MddBootstrapShutdown' || {
      echo "错误:产物缺少 Mdd* 导出" >&2
      exit 1
    }
fi

log "完成"
ls -l "$OUT"