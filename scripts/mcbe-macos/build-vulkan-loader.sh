#!/usr/bin/env bash
#
# 从源码构建 Khronos Vulkan-Loader(macOS x86_64)。
#
# 为什么不用 Homebrew:vulkan-headers / vulkan-loader 都没有 macOS Intel 的
# bottle(只有 arm64 与 Linux),而 Wine 的 winevulkan 在运行期要 dlopen
# libvulkan.1.dylib。MoltenVK 只提供 ICD(驱动),不含 loader,因此必须自建。
#
# 产物:$OUT/lib/libvulkan.1.dylib(以及同一实体的 libvulkan.dylib)
#
# 环境变量:
#   WORKDIR      工作目录(默认 <repo>/.work)
#   VULKAN_LOADER_REF  Vulkan-Loader 提交/标签(默认钉住的 tag)
#   OUT          产物目录(默认 <repo>/artifacts/runtime/vulkan-loader)
#   ARCH         macOS 架构(默认 x86_64)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKDIR="${WORKDIR:-$REPO_ROOT/.work}"
VULKAN_LOADER_REPO="${VULKAN_LOADER_REPO:-https://github.com/KhronosGroup/Vulkan-Loader.git}"
VULKAN_HEADERS_REPO="${VULKAN_HEADERS_REPO:-https://github.com/KhronosGroup/Vulkan-Headers.git}"
VULKAN_LOADER_REF="${VULKAN_LOADER_REF:-vulkan-sdk-1.4.313.0}"
OUT="${OUT:-$REPO_ROOT/artifacts/runtime/vulkan-loader}"
ARCH="${ARCH:-x86_64}"
SRC="$WORKDIR/Vulkan-Loader"
HEADERS_SRC="$WORKDIR/Vulkan-Headers"
HEADERS_PREFIX="$WORKDIR/vulkan-headers-install"
BUILD="$SRC/build-${ARCH}"

log() { printf '\n==> %s\n' "$*"; }

# checkout_pinned <仓库目录> <仓库地址> <ref>:按 ref 检出;ref 不存在时回退到默认分支。
checkout_pinned() {
  local dir="$1" repo="$2" ref="$3"
  if [ ! -d "$dir/.git" ]; then
    rm -rf "$dir"
    git clone -q "$repo" "$dir"
  fi
  git -C "$dir" remote set-url origin "$repo"
  git -C "$dir" fetch -q --tags origin
  if git -C "$dir" rev-parse -q --verify "$ref^{commit}" >/dev/null 2>&1; then
    git -C "$dir" checkout -q -f "$ref"
  else
    echo "警告:$repo 取不到 $ref,回退到默认分支" >&2
    local default
    default="$(git -C "$dir" symbolic-ref -q --short refs/remotes/origin/HEAD 2>/dev/null || echo origin/main)"
    git -C "$dir" checkout -q -f "${default#origin/}"
    git -C "$dir" pull -q --ff-only || true
  fi
  echo "$(basename "$dir") HEAD: $(git -C "$dir" rev-parse HEAD)"
}

log "准备 Vulkan-Headers(loader 的 CMake 依赖)"
checkout_pinned "$HEADERS_SRC" "$VULKAN_HEADERS_REPO" "$VULKAN_LOADER_REF"
rm -rf "$HEADERS_PREFIX"
cmake -S "$HEADERS_SRC" -B "$HEADERS_SRC/build-install" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$HEADERS_PREFIX" >/dev/null
cmake --install "$HEADERS_SRC/build-install" >/dev/null
[ -f "$HEADERS_PREFIX/include/vulkan/vulkan.h" ] || { echo "错误:Vulkan-Headers 安装不完整" >&2; exit 1; }

log "获取源码 Vulkan-Loader@$VULKAN_LOADER_REF"
mkdir -p "$WORKDIR"
checkout_pinned "$SRC" "$VULKAN_LOADER_REPO" "$VULKAN_LOADER_REF"
git -C "$SRC" submodule update --init --recursive -q || true

log "configure(Vulkan-Loader,$ARCH)"
rm -rf "$BUILD"
cmake -S "$SRC" -B "$BUILD" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
  -DVULKAN_HEADERS_INSTALL_DIR="$HEADERS_PREFIX" \
  -DBUILD_TESTS=OFF \
  -DBUILD_WSI_XCB_SUPPORT=OFF \
  -DBUILD_WSI_XLIB_SUPPORT=OFF \
  -DBUILD_WSI_WAYLAND_SUPPORT=OFF \
  -DBUILD_WSI_DIRECTFB_SUPPORT=OFF \
  -DUPDATE_DEPS=OFF >/dev/null

log "构建"
cmake --build "$BUILD" --target vulkan -j"$(sysctl -n hw.activecpu)"

LOADER="$(find "$BUILD" -maxdepth 3 -name 'libvulkan.1.dylib' | head -1)"
[ -n "$LOADER" ] || { echo "错误:未找到 libvulkan.1.dylib" >&2; exit 1; }

log "安装名称规范化为 @rpath/libvulkan.1.dylib"
install_name_tool -id "@rpath/libvulkan.1.dylib" "$LOADER"

log "输出到 $OUT"
rm -rf "$OUT"
mkdir -p "$OUT/lib"
cp "$LOADER" "$OUT/lib/libvulkan.1.dylib"
chmod u+w "$OUT/lib/libvulkan.1.dylib"

log "校验依赖(只应依赖系统库)"
otool -L "$OUT/lib/libvulkan.1.dylib"

log "完成"
ls -l "$OUT/lib"
