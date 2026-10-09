#!/usr/bin/env bash
#
# 把预置移动端运行时装配到 src/MChub.Android/RuntimeAssets/，让**本地**构建出来的
# APK 也开箱即用（不必只依赖 CI 出的包）。
#
# 为什么要内嵌：Android 侧要跑 MC Java 版，得有移植版 OpenJDK + Pojav 系 native +
# 移动端 LWJGL。这些都由 .github/workflows/build-mobile-runtime.yml 构建成
# release 资产 mobile-runtime-arm64.zip；本脚本把它取下来按 APK 需要的布局摊平。
#
# 用法：
#   scripts/prepare-android-runtime.sh              # 缺啥拉啥
#   scripts/prepare-android-runtime.sh --refresh     # 强制重新下载
#
# 之后照常构建即可（RuntimeAssets/ 已被 .gitignore 忽略）：
#   dotnet build src/MChub.Android/MChub.Android.csproj -c Debug \
#     -p:EmbedAssembliesIntoApk=true
#
# 与 CI（.github/workflows/build-android.yml 的 "Fetch prebuilt mobile runtime"）保持一致，
# 改布局时两边要一起改。

set -euo pipefail

RUNTIME_TAG="${RUNTIME_TAG:-mobile-runtime-v1}"
ASSET_NAME="mobile-runtime-arm64.zip"
REPO_SLUG="${REPO_SLUG:-}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dest="$repo_root/src/MChub.Android/RuntimeAssets"
work_dir="${TMPDIR:-/tmp}/mchub-runtime"
zip_path="$work_dir/$ASSET_NAME"

refresh=false
[ "${1:-}" = "--refresh" ] && refresh=true

# 仓库名以 origin 为准（组织改过名：CodeHub-develop → Hub-code-develop）。
# 注意用**兼容 BSD/macOS 的 sed**：`+?` 非贪婪在 BSD sed 上会报 repetition-operator invalid。
remote_url="$(git -C "$repo_root" remote get-url origin 2>/dev/null || true)"
repo_slug="$REPO_SLUG"
if [ -z "$repo_slug" ]; then
    repo_slug="$(printf '%s' "$remote_url" | sed -E 's#^.*[:/]([^/]+/[^/]+)$#\1#' | sed 's#\.git$##')"
fi
if [ -z "$repo_slug" ]; then
    echo "取不到 origin 仓库名（$remote_url），请设 REPO_SLUG=owner/name 后重跑" >&2
    exit 1
fi

if [ ! -d "$work_dir" ] || [ "$refresh" = true ]; then
    rm -rf "$work_dir"
fi
mkdir -p "$work_dir"

if [ ! -f "$zip_path" ] || [ "$refresh" = true ]; then
    echo "==> 下载 $ASSET_NAME（$RUNTIME_TAG，来自 $repo_slug）"
    gh release download "$RUNTIME_TAG" \
        --repo "$repo_slug" \
        --pattern "$ASSET_NAME" \
        --dir "$work_dir" \
        --clobber
fi

extract_dir="$work_dir/unpacked"
rm -rf "$extract_dir"
mkdir -p "$extract_dir"
echo "==> 解包"
unzip -q "$zip_path" -d "$extract_dir"

echo "==> 装配到 $dest"
rm -rf "$dest"
mkdir -p "$dest"

# 原生库打成一个 zip 进 assets —— **不进 APK 的 lib/<abi>/**：
#   * 平台会对 lib/<abi>/*.so 做 16 KB 页对齐检查，这批上游预编译产物（AAR / JRE 自带）是 4 KB 对齐，
#     放进去会让 App 被强制「页面大小兼容模式」并在启动时弹警告（实测过）；
#   * 放进 lib/ 还会被 .NET Android 在启动时全部 loadLibrary，触发上游 native 的 JNI_OnLoad。
# 运行时解到私有目录后，由 JREUtils.loadNativeLayer（JNI 部分）与 PojavRuntimeSupport
# （GL 翻译层等）按绝对路径装载。
(cd "$extract_dir/natives" && zip -qr "$dest/natives.zip" .)

# LWJGL natives 与 LWJGL jar 同样打 zip 进 assets：
#   * assets 里的裸 .so 会被 .NET Android 检查 ABI（路径无 ABI 名 → XA4301）
#   * assets 里的 .jar 会被当成 AndroidJavaLibrary 参与 dex（两套版本同名 jar 内容不同 → XA1014）
# 它们只是运行时解压出来的普通文件，不需要进 dex。
if [ -d "$extract_dir/lwjgl-natives" ]; then
    mkdir -p "$dest/lwjgl-natives"
    for d in "$extract_dir"/lwjgl-natives/*/; do
        ver="$(basename "$d")"
        (cd "$d" && zip -qr "$dest/lwjgl-natives/$ver.zip" .)
    done
fi

cp -r "$extract_dir/jre" "$dest/jre"

if [ -d "$extract_dir/lwjgl" ]; then
    mkdir -p "$dest/lwjgl"
    for d in "$extract_dir"/lwjgl/*/; do
        ver="$(basename "$d")"
        (cd "$d" && zip -qr "$dest/lwjgl/$ver.zip" .)
    done
fi

cp "$extract_dir/manifest.json" "$dest/manifest.json"

echo
echo "=== 预置运行时 ==="
echo "natives zip    : $(unzip -l "$dest/natives.zip" | tail -1 | awk '{print $2}') 个条目（$(du -h "$dest/natives.zip" | cut -f1)）"
echo "lwjgl-natives  : $(ls -1 "$dest/lwjgl-natives" 2>/dev/null | tr '\n' ' ')"
echo "lwjgl jar      : $(ls -1 "$dest/lwjgl" 2>/dev/null | tr '\n' ' ')"
echo "jre            : $(ls -1 "$dest/jre" 2>/dev/null | tr '\n' ' ')"
echo "总计           : $(du -sh "$dest" | cut -f1)"
