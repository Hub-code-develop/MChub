#!/usr/bin/env bash
#
# 汇总各来源的产物为统一的运行时树,并对整棵树做自检。
# 输入(默认均位于 <repo>/artifacts/runtime 之下):
#   wine/         由 scripts/build-wine.sh 产出
#   xodus/        由 scripts/build-xodus-service.sh 产出
#   render/       由 scripts/fetch-render-stack.sh 产出
#   winappsdk/    由 scripts/build-winappsdk-stub.sh 产出
#   vulkan-loader/由 scripts/build-vulkan-loader.sh 产出
#
# 产物:<repo>/artifacts/runtime/
#   wine/{bin,lib,share}            Wine(WineGDK) 安装树
#   xodus/xodus-service             原生 macOS Xbox 服务
#   render/{lib,vulkan,dxvk,vkd3d-proton}
#   winappsdk/Microsoft.WindowsAppRuntime.Bootstrap.dll  引导器桩(见 winappsdk-stub/bootstrap_stub.c)
#   RUNTIME-MANIFEST.txt

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUNTIME="${RUNTIME:-$REPO_ROOT/artifacts/runtime}"

log() { printf '\n==> %s\n' "$*"; }
fail() { echo "错误:$*" >&2; exit 1; }

log "自检运行时树:$RUNTIME"
[ -x "$RUNTIME/wine/bin/wine" ]                                   || fail "缺少 wine/bin/wine"
[ -f "$RUNTIME/wine/lib/wine/x86_64-windows/xgameruntime.dll" ]    || fail "缺少 xgameruntime.dll(PE)"
[ -f "$RUNTIME/wine/lib/wine/x86_64-unix/xgameruntime.so" ]        || fail "缺少 xgameruntime.so(unix)"
[ -x "$RUNTIME/xodus/xodus-service" ]                              || fail "缺少 xodus-service"
[ -f "$RUNTIME/render/lib/libMoltenVK.dylib" ]                     || fail "缺少 libMoltenVK.dylib"
[ -f "$RUNTIME/render/vulkan/icd.d/MoltenVK_icd.json" ]            || fail "缺少 MoltenVK ICD 清单"
[ -f "$RUNTIME/render/dxvk/d3d11.dll" ]                            || fail "缺少 DXVK d3d11.dll"
[ -f "$RUNTIME/render/vkd3d-proton/d3d12.dll" ]                    || fail "缺少 vkd3d-proton d3d12.dll"
[ -f "$RUNTIME/winappsdk/Microsoft.WindowsAppRuntime.Bootstrap.dll" ] || fail "缺少 WinAppSDK Bootstrapper 桩"

log "收集 Wine 运行期依赖的 macOS dylib(Vulkan loader / FreeType)"
RENDER_LIB="$RUNTIME/render/lib"
mkdir -p "$RENDER_LIB"

# vulkan-loader 在 macOS Intel 上没有 bottle,由 build-vulkan-loader.sh 自建。
brew install --quiet freetype
BREW="$(brew --prefix)"

# Wine 的 unix 侧按 soname dlopen(win32u.so 里能看到确切名字)。把 dylib 及其
# Homebrew 依赖一并收进 render/lib:WineEnvironment 会把它加进
# DYLD_FALLBACK_LIBRARY_PATH,dyld 在构建机的绝对路径(/usr/local/...)失效后
# 会按 basename 回退到这里,所以依赖的绝对 install name 不影响分发。
bundle_dylib() { # bundle_dylib <源 dylib>
  local src="$1"
  [ -f "$src" ] || return 1
  local queue=("$src")
  while [ ${#queue[@]} -gt 0 ]; do
    local lib="${queue[0]}"
    queue=("${queue[@]:1}")
    local base
    base="$(basename "$lib")"
    if [ ! -f "$RENDER_LIB/$base" ]; then
      cp -f "$lib" "$RENDER_LIB/$base"
      chmod u+w "$RENDER_LIB/$base"
    fi
    while IFS= read -r dep; do
      case "$dep" in
        "$BREW"/*) ;;
        *) continue ;;
      esac
      [ -f "$dep" ] || continue
      [ -f "$RENDER_LIB/$(basename "$dep")" ] && continue
      queue+=("$dep")
    done < <(otool -L "$lib" | tail -n +2 | awk '{print $1}')
  done
}

soname_of() { # soname_of <dylib 名正则>;从 wine 的 unix 侧模块里找出它实际 dlopen 的 soname
  local found
  found="$(grep -ah -oE "$1" "$RUNTIME"/wine/lib/wine/x86_64-unix/*.so 2>/dev/null | head -1 || true)"
  printf '%s' "$found"
}

VULKAN_SONAME="$(soname_of 'libvulkan[.0-9]*\.dylib')"
VULKAN_SONAME="${VULKAN_SONAME:-libvulkan.1.dylib}"
VULKAN_SRC="$(ls "$RUNTIME"/vulkan-loader/lib/libvulkan.*.dylib 2>/dev/null | head -1 || true)"
[ -n "$VULKAN_SRC" ] || fail "找不到自建 Vulkan loader(build-vulkan-loader.sh 产物)"
cp -f "$VULKAN_SRC" "$RENDER_LIB/$VULKAN_SONAME"
chmod u+w "$RENDER_LIB/$VULKAN_SONAME"
echo "Vulkan loader:$VULKAN_SONAME"

FREETYPE_SONAME="$(soname_of 'libfreetype[.0-9]*\.dylib')"
FREETYPE_SONAME="${FREETYPE_SONAME:-libfreetype.6.dylib}"
FREETYPE_SRC="$(ls "$BREW"/opt/freetype/lib/libfreetype.*.dylib 2>/dev/null | head -1 || true)"
[ -n "$FREETYPE_SRC" ] || fail "找不到 FreeType(brew freetype)"
bundle_dylib "$FREETYPE_SRC"
[ -f "$RENDER_LIB/$FREETYPE_SONAME" ] || cp -f "$FREETYPE_SRC" "$RENDER_LIB/$FREETYPE_SONAME"
echo "FreeType:$FREETYPE_SONAME"

log "关键检查:wine 内是否自带 GDK 组件"
ls -1 "$RUNTIME/wine/lib/wine/x86_64-windows" | grep -i -E 'xgameruntime|xgameruntime' || true
ls -1 "$RUNTIME/wine/lib/wine/x86_64-unix" | grep -i xgameruntime || true

log "检查 Wine 版本"
"$RUNTIME/wine/bin/wine" --version 2>/dev/null || echo "提示:无法在本机直接运行 wine(不影响打包)"

log "校验随包 dylib 已就位"
[ -f "$RENDER_LIB/$VULKAN_SONAME" ]   || fail "Vulkan loader 未收进 render/lib"

log "写入清单"
{
  echo "generated-at: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "wine-ref: ${WINE_GDK_REF:-unknown}"
  echo "xodus-ref: ${XODUS_REF:-unknown}"
  echo "moltenvk: ${MOLTENVK_VERSION:-unknown}"
  echo "dxvk: ${DXVK_VERSION:-unknown}"
  echo "vkd3d-proton: ${VKD3D_PROTON_VERSION:-unknown}"
  echo "vulkan-loader: $VULKAN_SONAME"
  echo "freetype: $FREETYPE_SONAME"
  echo
  echo "wine:"
  find "$RUNTIME/wine" -type f | wc -l | tr -d ' '
  echo "render:"
  find "$RUNTIME/render" -type f | wc -l | tr -d ' '
} > "$RUNTIME/RUNTIME-MANIFEST.txt"

cat "$RUNTIME/RUNTIME-MANIFEST.txt"
log "总计:$(du -sh "$RUNTIME" | cut -f1)"