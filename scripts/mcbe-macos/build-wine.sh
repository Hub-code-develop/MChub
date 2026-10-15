#!/usr/bin/env bash
#
# 在 macOS(x86_64)上构建 WineGDK 分支的 Wine,产出可重定位的 wine 运行时树。
#
# 产物:$OUT/{bin,lib,share} —— 即一份完整的 wine 安装树(bin/wine 与 lib/wine 必须保持同级)。
# 配方对齐上游 tools/gitlab/build-mac,但改为完整 install(上游只 install-lib 供测试用)。
#
# 环境变量:
#   WORKDIR       工作目录(默认 <repo>/.work)
#   WINE_GDK_REF  WineGDK 提交(默认钉住的 master SHA)
#   OUT           产物目录(默认 <repo>/artifacts/runtime/wine)
#   SKIP_DEPS     设为 1 跳过 brew install

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKDIR="${WORKDIR:-$REPO_ROOT/.work}"
WINE_GDK_REPO="${WINE_GDK_REPO:-https://github.com/Weather-OS/WineGDK.git}"
WINE_GDK_REF="${WINE_GDK_REF:-b03ba49c4f326c36aa6930fbe6cf72841ef3738c}"
OUT="${OUT:-$REPO_ROOT/artifacts/runtime/wine}"
SRC="$WORKDIR/WineGDK"
STAGE="$WORKDIR/wine-stage"
BUILD="$SRC/build64"

log() { printf '\n==> %s\n' "$*"; }

BREW="$(brew --prefix)"
log "Homebrew 根目录:$BREW"

if [ "${SKIP_DEPS:-0}" != "1" ]; then
  log "安装构建依赖"
  # Vulkan:configure 对 vulkan 的检测是一次"无头链接检查"——它只编译一个声明了
  # vkGetInstanceProcAddr 的 conftest 再链接 -lvulkan(Wine 源码里所有 include 都走
  # 自带的 include/wine/vulkan.h,不需要系统头文件)。链接不上就转而试 -lMoltenVK;
  # 两者都没有时,因为传了 --with-vulkan,configure 会直接报错退出。
  # macOS 上 Wine 只在运行期 dlopen(darwin 分支把 SONAME_LIBVULKAN 定义成检测到的
  # soname,并不真的链接它),所以 loader 由 build-vulkan-loader.sh 自建,构建前通过
  # VULKAN_LOADER_DIR 提供,再随运行时分发。
  # freetype/fontconfig 供字体渲染(运行期 dylib 由 assemble-runtime.sh 收进 render/lib)。
  brew install --quiet mingw-w64 autoconf automake libtool bison pkg-config \
    freetype fontconfig gnutls gettext zstd
fi

MINGW="$BREW/opt/mingw-w64"
BISON_BIN="$BREW/opt/bison/bin/bison"

log "检查 mingw-w64 交叉编译器"
"$MINGW/bin/x86_64-w64-mingw32-gcc" --version | head -1
"$MINGW/bin/x86_64-w64-mingw32-g++" --version | head -1

export PATH="$MINGW/bin:$BREW/opt/bison/bin:$BREW/bin:$PATH"
export PKG_CONFIG_PATH="$BREW/opt/freetype/lib/pkgconfig:$BREW/opt/fontconfig/lib/pkgconfig:$BREW/opt/gnutls/lib/pkgconfig:$BREW/lib/pkgconfig:$BREW/share/pkgconfig"

# configure 的 vulkan 检测要求能链接到 libvulkan.dylib(见上面依赖段说明)。
# 自建 loader 由 CI 提前下载到这里;缺失就让 configure 立刻失败,而不是编译完才发现。
VULKAN_LDFLAGS=""
if [ -n "${VULKAN_LOADER_DIR:-}" ]; then
  [ -f "$VULKAN_LOADER_DIR/lib/libvulkan.dylib" ] || {
    echo "错误:VULKAN_LOADER_DIR=$VULKAN_LOADER_DIR 下缺少 lib/libvulkan.dylib" >&2; exit 1; }
  VULKAN_LDFLAGS="-L$VULKAN_LOADER_DIR/lib"
  log "Vulkan loader 参与 configure 检测:$VULKAN_LOADER_DIR/lib"
else
  log "警告:未提供 VULKAN_LOADER_DIR,configure 需自行找到 libvulkan/libMoltenVK"
fi

log "获取源码 WineGDK@$WINE_GDK_REF"
mkdir -p "$WORKDIR"
if [ ! -d "$SRC/.git" ]; then
  git init -q "$SRC"
  git -C "$SRC" remote add origin "$WINE_GDK_REPO"
fi
git -C "$SRC" remote set-url origin "$WINE_GDK_REPO"
# 先按 SHA 浅取;仓库以 master 头部为钉点时两种取法等价。
if ! git -C "$SRC" fetch --depth 1 -q origin "$WINE_GDK_REF" 2>/dev/null; then
  echo "按 SHA 取源码失败,回退到默认分支"
  git -C "$SRC" fetch --depth 1 -q origin master
fi
git -C "$SRC" checkout -q -f FETCH_HEAD
git -C "$SRC" fetch --depth 1 -q --tags origin || true
git config --global --add safe.directory "$SRC" || true
echo "HEAD: $(git -C "$SRC" rev-parse HEAD)"

# 上游 master 的 dlls/gameinput/padinput.c 没写进 dlls/gameinput/Makefile.in 的 SOURCES（WIP 文件，
# 引用了尚未定义的结构体 game_input_device / game_input_reading）。make_makefiles 会把目录下所有 .c
# 自动补进 SOURCES，于是它被编译并报 "invalid use of undefined type 'struct game_input_device'"。
# 该文件本不属于 gameinput.dll，构建前移除。
rm -f "$SRC/dlls/gameinput/padinput.c"

# 上游 xgameruntime 的 QueryApiImpl 只实现了 XSystem / XGameRuntimeFeature /
# XSystemAnalytics / XNetworking / XThreading,XUser 落到兜底分支返回 E_NOTIMPL。
# 基岩版启动早期就会 QueryApiImpl(CLSID_XUserImpl),拿不到接口时其 C++ 侧抛未处理
# 异常并 abort(退出码 3),表现为账户未登录时闪退。private.h 已声明
# `extern IXUserImpl *x_user;`(定义于 GDKComponent/System/XUser.cpp),补一个分支
# 直接返回它即可:未登录时该实现的各方法返回 E_NOTIMPL,但足以让游戏继续启动到
# 窗口出现,从而满足"未登录也允许启动"。
log "补丁:QueryApiImpl 支持 CLSID_XUserImpl(免登录启动)"
python3 - "$SRC/dlls/xgameruntime/main.c" <<'PYEOF'
import sys

path = sys.argv[1]
anchor = """    else if ( IsEqualGUID( runtimeClassId, &CLSID_XNetworkingImpl ) )
    {
        return IXNetworkingImpl_QueryInterface( x_networking, interfaceId, out );
    }
"""
addition = """    else if ( IsEqualGUID( runtimeClassId, &CLSID_XUserImpl ) )
    {
        return IXUserImpl_QueryInterface( x_user, interfaceId, out );
    }
"""
src = open(path).read()
if "CLSID_XUserImpl" in src:
    print("main.c 已含 XUser 分支,跳过")
elif anchor not in src:
    sys.exit("未找到 XNetworking 分支锚点,无法为 QueryApiImpl 打 XUser 补丁")
else:
    open(path, "w").write(src.replace(anchor, anchor + addition, 1))
    print("已为 QueryApiImpl 补上 CLSID_XUserImpl 分支")
PYEOF

# 上游 XUser.cpp 的 XUserAddAsync/XUserAddResult 是纯桩:直接返回 E_NOTIMPL,且
# 从不完成调用方传入的异步块。基岩版启动时以
# XUserAddOptions_AddDefaultUserSilently(0x01) 调 XUserAddAsync,紧接着调
# XUserAddResult 取结果;拿到 E_NOTIMPL 后它的 C++ 侧抛未处理异常并 abort
# (退出码 3,窗口虽已创建但随即退出:dwmapi/WTSRegisterSessionNotification 之后
# 连续 44 层 __processing_throw 再 raise)。GDK 的文档语义是:找不到默认用户时该
# 异步操作以 E_GAMEUSER_NO_DEFAULT_USER(0x89245106) 结束,标题据此进入未登录流程
# —— Windows 上基岩版"未登录也能玩"依赖的正是这条路径。WineGDK 自带完整的
# XAsync 实现(XAsyncBegin/Schedule/Complete/GetResult,XAsyncRunProvider 即范例),
# 因此按 provider 模式补上即可,无需在 MChub 侧另注入 QueryApiImpl hook。
log "补丁:XUser 未登录时按 GDK 语义完成 XUserAddAsync(免登录启动)"
python3 - "$SRC/dlls/xgameruntime/GDKComponent/System/XUser.cpp" <<'PYEOF'
import sys

path = sys.argv[1]
src = open(path).read()

provider = """/* Xbox GDK semantics: when AddDefaultUserSilently finds no default user, the async
 * operation completes with E_GAMEUSER_NO_DEFAULT_USER so the title can continue in a
 * "not signed in" state. The upstream stub returned E_NOTIMPL and never completed the
 * async block, which makes Bedrock Edition throw an unhandled C++ exception and abort
 * (exit code 3). */
static HRESULT __stdcall XUserAddProvider( XAsyncOp op, const XAsyncProviderData *data )
{
    switch (op)
    {
        case XAsyncOp::Begin:
            return XAsyncSchedule( data->async, 0 );

        case XAsyncOp::DoWork:
            XAsyncComplete( data->async, E_GAMEUSER_NO_DEFAULT_USER, 0 );
            return S_OK;

        case XAsyncOp::Cancel:
        case XAsyncOp::Cleanup:
        case XAsyncOp::GetResult:
            break;
    }

    return S_OK;
}

"""

class_anchor = "class XUserImpl :"

old_impl = """    HRESULT WINAPI XUserAddAsync( XUserAddOptions options, XAsyncBlock *async ) override
    {
        FIXME( "options %d, async %p stub!\\n", (int)options, async );
        return E_NOTIMPL;
    }

    HRESULT WINAPI XUserAddResult( XAsyncBlock *async, XUserHandle *newUser ) override
    {
        FIXME( "async %p, newUser %p stub!\\n", async, newUser );
        return E_NOTIMPL;
    }
"""

new_impl = """    HRESULT WINAPI XUserAddAsync( XUserAddOptions options, XAsyncBlock *async ) override
    {
        TRACE( "options %d, async %p.\\n", (int)options, async );

        /* No signed-in Xbox user exists in this runtime (a real session is handled
         * outside of Wine by MChub). Complete the async block with the documented
         * "no default user" result instead of leaving it pending, so the title can
         * start in a not-signed-in state. */
        if (!async)
            return E_POINTER;

        return XAsyncBegin( async, nullptr, (const void *)XUserAddProvider, "XUserAddAsync", XUserAddProvider );
    }

    HRESULT WINAPI XUserAddResult( XAsyncBlock *async, XUserHandle *newUser ) override
    {
        TRACE( "async %p, newUser %p.\\n", async, newUser );

        if (!newUser)
            return E_POINTER;

        *newUser = nullptr;

        return XAsyncGetResult( async, (const void *)XUserAddProvider, sizeof(XUserHandle), newUser, nullptr );
    }
"""

if "XUserAddProvider" in src:
    print("XUser.cpp 已含 XUserAddProvider,跳过")
elif class_anchor not in src or old_impl not in src:
    sys.exit("未找到 XUser.cpp 的补丁锚点,无法为 XUserAddAsync 打补丁")
else:
    src = src.replace(class_anchor, provider + class_anchor, 1)
    src = src.replace(old_impl, new_impl, 1)
    open(path, "w").write(src)
    print("已按 GDK 语义实现 XUserAddAsync/XUserAddResult(未登录返回 E_GAMEUSER_NO_DEFAULT_USER)")
PYEOF

log "生成 configure / Makefile / spec"
cd "$SRC"
./tools/make_requests
./tools/make_specfiles
./tools/make_makefiles
autoreconf -f

log "configure(win64 + mingw 交叉编译)"
rm -rf "$BUILD"
mkdir -p "$BUILD"
cd "$BUILD"
../configure -C --enable-win64 --with-mingw \
  --prefix=/usr/local \
  --with-vulkan \
  --disable-tests \
  --without-alsa --without-capi --without-cups --without-dbus --without-gphoto \
  --without-gssapi --without-gstreamer --without-hwloc --without-inotify \
  --without-krb5 --without-netapi --without-opencl --without-oss --without-pcap \
  --without-pcsclite --without-pulse --without-sane --without-sdl --without-udev \
  --without-usb --without-v4l2 --without-wayland \
  BISON="$BISON_BIN" \
  CPPFLAGS="-I$BREW/include" \
  LDFLAGS="-L$BREW/lib $VULKAN_LDFLAGS"

# 尽早确认 SONAME_LIBVULKAN 已定义。没定义的话 win32u 会编成
# "built without Vulkan support",而此时 make 还没跑,失败成本最低。
if ! grep -q '^#define SONAME_LIBVULKAN' "$BUILD/include/config.h"; then
  echo "错误:configure 未定义 SONAME_LIBVULKAN,说明 vulkan 检测失败。" >&2
  echo "      确认 VULKAN_LOADER_DIR 指向含 lib/libvulkan.dylib 的目录。" >&2
  grep -n -i 'vulkan' "$BUILD/config.log" | tail -20 >&2 || true
  exit 1
fi
log "configure 已定义 SONAME_LIBVULKAN:$(grep '^#define SONAME_LIBVULKAN' "$BUILD/include/config.h")"

log "make -j$(sysctl -n hw.activecpu)"
make -s -j"$(sysctl -n hw.activecpu)"

log "make install -> $STAGE"
rm -rf "$STAGE"
make -s install DESTDIR="$STAGE"

log "校验关键产物"
test -f "$STAGE/usr/local/lib/wine/x86_64-windows/xgameruntime.dll"
test -f "$STAGE/usr/local/lib/wine/x86_64-unix/xgameruntime.so"

if command -v strings >/dev/null 2>&1; then
  log "校验 Vulkan 支持已编入(DXVK/vkd3d-proton 依赖)"
  if strings "$STAGE/usr/local/lib/wine/x86_64-unix/win32u.so" | grep -q "built without Vulkan"; then
    echo "错误:Wine 未启用 Vulkan 支持(确认 vulkan-loader/vulkan-headers 已安装且 configure 未被跳过)" >&2
    exit 1
  fi
  # unix 侧按 soname dlopen,assemble-runtime.sh 会据此把对应 dylib 收进 render/lib。
  strings "$STAGE/usr/local/lib/wine/x86_64-unix/win32u.so" | grep -E '^libvulkan.*\.dylib$' | sort -u || true
  strings "$STAGE/usr/local/lib/wine/x86_64-unix/win32u.so" | grep -E '^libfreetype.*\.dylib$' | sort -u || true
fi

log "裁剪并组装到 $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"
cp -a "$STAGE/usr/local/." "$OUT/"

# 去掉不需要的内容:开发头文件、静态库、man、桌面集成文件。
rm -rf "$OUT/include" "$OUT/share/man" "$OUT/share/applications" "$OUT/share/icons"
find "$OUT" -name '*.a' -delete
find "$OUT" -name '*.la' -delete
find "$OUT" -name '*.def' -delete

log "strip"
find "$OUT/lib/wine/x86_64-unix" -type f -name '*.so' -exec strip -x {} + 2>/dev/null || true
find "$OUT/lib/wine/x86_64-windows" -type f -name '*.dll' \
  -exec "$MINGW/bin/x86_64-w64-mingw32-strip" --strip-unneeded {} + 2>/dev/null || true
strip -x "$OUT/bin/wine" 2>/dev/null || true
find "$OUT/bin" -type f ! -name 'wine' -exec strip -x {} + 2>/dev/null || true

log "完成,$(du -sh "$OUT" | cut -f1)"
"$OUT/bin/wine" --version || echo "警告:当前环境无法直接执行 wine(--version 失败),产物已生成"