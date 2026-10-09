#!/usr/bin/env bash
# 用本机 Android NDK 构建 native 启动层 libmchubjvm.so。
#
# 用法：
#   ./build-android.sh                # 默认 arm64-v8a
#   ./build-android.sh x86_64         # 给模拟器用
#
# 环境变量：
#   ANDROID_SDK_ROOT   默认 ~/Library/Android/sdk
#   ANDROID_NDK_HOME   默认取 SDK 下版本号最大的 ndk
#   ANDROID_MIN_SDK    默认 26（与 csproj 的 SupportedOSPlatformVersion 一致）
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ABI="${1:-arm64-v8a}"
API="${ANDROID_MIN_SDK:-26}"

# SDK 位置：环境变量优先，其次按常见安装路径探测（macOS / Linux CI 都覆盖）。
SDK="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
if [ -z "$SDK" ]; then
  for candidate in "$HOME/Library/Android/sdk" "$HOME/Android/Sdk" "/usr/local/lib/android/sdk"; do
    if [ -d "$candidate" ]; then
      SDK="$candidate"
      break
    fi
  done
fi
NDK="${ANDROID_NDK_HOME:-$(ls -d "${SDK:-/nonexistent}"/ndk/* 2>/dev/null | sort -V | tail -1 || true)}"
if [ -z "${NDK:-}" ] || [ ! -d "$NDK" ]; then
  echo "找不到 Android NDK：设置 ANDROID_NDK_HOME，或把 NDK 装到 \$ANDROID_SDK_ROOT/ndk" >&2
  exit 1
fi

CMAKE="$(command -v cmake || echo "$SDK/cmake/3.22.1/bin/cmake")"
if [ ! -x "$CMAKE" ]; then
  echo "找不到 cmake：安装 cmake 或使用 SDK 自带版本" >&2
  exit 1
fi

BUILD_DIR="$HERE/out/$ABI"
JOBS="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"

echo "NDK   : $NDK"
echo "ABI   : $ABI (android-$API)"
echo "输出  : $BUILD_DIR/libmchubjvm.so"

"$CMAKE" -S "$HERE" -B "$BUILD_DIR" \
  -DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI="$ABI" \
  -DANDROID_PLATFORM="android-$API" \
  -DCMAKE_BUILD_TYPE=Release >/dev/null

"$CMAKE" --build "$BUILD_DIR" -j "$JOBS"

ls -l "$BUILD_DIR/libmchubjvm.so"

# 自检：必须 16 KB 段对齐（Android 15+ 对 lib/<abi>/ 下 .so 的硬要求；不达标装机弹「页面大小兼容模式」警告）。
# CMakeLists 已显式加 -Wl,-z,max-page-size=16384，这里再验一次，免得换 NDK 时静默退化。
READELF="$(ls -d "$NDK"/toolchains/llvm/prebuilt/*/bin/llvm-readelf 2>/dev/null | head -1 || true)"
if [ -n "${READELF:-}" ] && [ -x "$READELF" ]; then
  BAD="$("$READELF" -l "$BUILD_DIR/libmchubjvm.so" | awk '$1=="LOAD" && $NF!="0x4000"{print}')"
  if [ -n "$BAD" ]; then
    echo "✗ libmchubjvm.so 的 LOAD 段不是 16 KB 对齐（Android 15+ 会把 App 判为不符合要求）：" >&2
    echo "$BAD" >&2
    exit 1
  fi
  echo "对齐  : 16 KB ok"
fi
