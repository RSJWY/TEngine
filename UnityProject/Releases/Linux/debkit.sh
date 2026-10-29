#!/usr/bin/env bash
#
# debkit - Unity/Linux 桌面程序一键打 deb 工具
#
#   ./debkit.sh -c appConfigs/example.conf
#   ./debkit.sh -c appConfigs/xxx.conf -v 1.2.0
#
# 产物: output/<包名>_<版本>_<架构>.deb
#
set -Eeuo pipefail

DEBKIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# 通过 /usr/bin/debkit 调用时 BASH_SOURCE 指向真实脚本, 所以这里总是对的

# ---------------------------------------------------------------------------
# 配置目录
#   从压缩包/共享目录跑: 脚本目录可写, 配置就放 appConfigs/
#   从 deb 装的系统目录跑: 脚本目录只读, 用户配置写到 ~/.config/debkit/appConfigs
#   两者都参与查找, 但只有可写的那份用于保存
# ---------------------------------------------------------------------------
SYS_APPS_DIR="$DEBKIT_DIR/appConfigs"
if [ -w "$SYS_APPS_DIR" ] 2>/dev/null; then
    APPS_DIR="$SYS_APPS_DIR"
else
    APPS_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/debkit/appConfigs"
    mkdir -p "$APPS_DIR" 2>/dev/null || true
fi

# 按「绝对路径 → 当前用户配置目录 → 系统预置目录」找配置文件,
# 所以装了 deb 之后可以 debkit -c example 这样只写名字
resolve_conf() {
    local v="$1" c
    [ -f "$v" ] && { printf '%s\n' "$v"; return 0; }
    case "$v" in */*) return 1 ;; esac
    for c in "$APPS_DIR/$v" "$APPS_DIR/$v.conf" \
             "$SYS_APPS_DIR/$v" "$SYS_APPS_DIR/$v.conf"; do
        [ -f "$c" ] && { printf '%s\n' "$c"; return 0; }
    done
    return 1
}

# shellcheck source=lib/common.sh
source "$DEBKIT_DIR/lib/common.sh"
# shellcheck source=lib/icons.sh
source "$DEBKIT_DIR/lib/icons.sh"
# shellcheck source=lib/wizard.sh
source "$DEBKIT_DIR/lib/wizard.sh"
# shellcheck source=lib/progress.sh
source "$DEBKIT_DIR/lib/progress.sh"

# ---------------------------------------------------------------------------
# 参数解析
# ---------------------------------------------------------------------------
CONFIG="$(resolve_conf example 2>/dev/null || true)"
[ -n "$CONFIG" ] || CONFIG="$APPS_DIR/example.conf"
# 输出目录/暂存目录可用环境变量或 -o/-w 指定(大工程建议把暂存放到空间充足的分区)
OUTDIR="${DEBKIT_OUT:-$DEBKIT_DIR/output}"
WORKROOT="${DEBKIT_WORK:-$DEBKIT_DIR/work}"
CLI_VERSION=""; CLI_REVISION=""; CLI_SRC=""
CLI_COMPRESS=""; CLI_LEVEL=""; CLI_AUTOSTART=""
KEEP_STAGE=0; SKIP_BUILD=0; DO_CLEAN=0; LIST_ONLY=0; VERIFY=0
CHECK_ONLY=0
INTERACTIVE=0
PROGRESS="${PROGRESS:-auto}"     # auto | tty | line | off, 见 lib/progress.sh
ARGC=$#

usage() {
    cat <<'EOF'
debkit - 一键把程序目录打成 .deb 安装包

用法:
  ./debkit.sh              交互式向导(在里面选已有配置或新建)
  ./debkit.sh -c <配置>    直接按配置打包

选项:
  -i, --interactive        进入交互式向导
  -c, --config <文件>      应用配置文件 (默认 appConfigs/example.conf)
  -v, --version <版本>     覆盖配置里的 VERSION
  -r, --revision <版本>    覆盖配置里的 REVISION
  -s, --source <目录>      覆盖源码目录 SRC_DIR
  -o, --outdir <目录>      输出目录 (默认 ./output)
  -w, --workdir <目录>     暂存目录 (默认 ./work, 大工程建议放到空间大的分区)
  -Z, --compress <类型>    pigz | gzip | xz | zstd | none | auto (默认取配置, auto 优先多线程 pigz)
  -z, --level <1-9>        压缩级别 (默认取配置, 一般是 6; 1 更快但体积略大)
      --autostart <yes|no> 覆盖"安装后默认开机自启"
  -k, --keep-stage         保留打包暂存目录(排查问题时用)
      --verify             强制逐文件校验产物(大包会多花几分钟)
      --no-build           只生成安装树, 不执行 dpkg-deb (调试用)
      --no-progress        关闭进度条(输出重定向到文件时更清爽)
      --progress           强制显示进度(默认: 终端里自动开, 重定向时每 5 秒一行)
      --clean              清空 work/ 与 output/ 后退出
  -l, --list               列出 appConfigs/ 下所有配置
      --check              检查本机打包环境依赖后退出(换机器先跑这个)
  -h, --help               显示本帮助

示例:
  ./debkit.sh -c appConfigs/example.conf
  ./debkit.sh -i                                  # 问答式新建配置并打包
  ./debkit.sh -c appConfigs/example.conf -v 1.1.0 -k
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        -c|--config)    CONFIG="$(resolve_conf "$2" 2>/dev/null || true)"
                        [ -n "$CONFIG" ] || CONFIG="$2"; shift 2 ;;
        -i|--interactive) INTERACTIVE=1; shift ;;
        -v|--version)   CLI_VERSION="$2"; shift 2 ;;
        -r|--revision)  CLI_REVISION="$2"; shift 2 ;;
        -s|--source)    CLI_SRC="$2"; shift 2 ;;
        -o|--outdir)    OUTDIR="$2"; shift 2 ;;
        -w|--workdir)   WORKROOT="$2"; shift 2 ;;
        -Z|--compress)  CLI_COMPRESS="$2"; shift 2 ;;
        -z|--level)     CLI_LEVEL="$2"; shift 2 ;;
        --autostart)    CLI_AUTOSTART="$2"; shift 2 ;;
        -k|--keep-stage) KEEP_STAGE=1; shift ;;
        --verify)       VERIFY=1; shift ;;
        --no-build)     SKIP_BUILD=1; shift ;;
        --clean)        DO_CLEAN=1; shift ;;
        -l|--list)      LIST_ONLY=1; shift ;;
        --check|--deps) CHECK_ONLY=1; shift ;;
        --no-progress)  PROGRESS=off; shift ;;
        --progress)     PROGRESS=on; shift ;;
        -h|--help)      usage; exit 0 ;;
        *) die "未知参数: $1 (用 -h 查看帮助)" ;;
    esac
done

pb_init

# ---------------------------------------------------------------------------
# 清理 / 列出配置
# ---------------------------------------------------------------------------
if [ "$DO_CLEAN" = 1 ]; then
    step "清理工作目录"
    rm -rf "$OUTDIR" "$WORKROOT" 2>/dev/null || true
    mkdir -p "$OUTDIR" "$WORKROOT" 2>/dev/null || true
    ok "已清空 $OUTDIR 与 $WORKROOT"
    exit 0
fi

if [ "$LIST_ONLY" = 1 ]; then
    step "可用配置"
    if [ "$APPS_DIR" = "$SYS_APPS_DIR" ]; then
        echo "  配置目录: $APPS_DIR"
    else
        # 系统目录只读(从 deb 装的), 用户配置另存一处
        echo "  用户配置: $APPS_DIR"
        echo "  系统预置: $SYS_APPS_DIR"
    fi
    found=0
    seen=""
    for f in "$APPS_DIR"/*.conf "$SYS_APPS_DIR"/*.conf; do
        [ -f "$f" ] || continue
        name=$(sed -n 's/^PKG_NAME="\{0,1\}\([^"]*\)"\{0,1\}.*/\1/p' "$f" | head -n1)
        title=$(sed -n 's/^APP_TITLE="\{0,1\}\([^"]*\)"\{0,1\}.*/\1/p' "$f" | head -n1)
        # 同名时用户目录优先, 系统那份不再重复列出
        case " $seen " in *" $(basename "$f") "*) continue ;; esac
        seen="$seen $(basename "$f")"
        found=1
        printf '  %-34s %s (%s)\n' "$(basename "$f")" "$title" "$name"
    done
    [ "$found" = 1 ] || warn "还没有配置文件, 用 debkit -i 新建一份"
    exit 0
fi

# ---------------------------------------------------------------------------
# 环境自检 (--check): 不加载配置, 只回答"这台机器能不能打包"
# ---------------------------------------------------------------------------
if [ "$CHECK_ONLY" = 1 ]; then
    step "打包环境自检"
    MISS=0
    line() { printf ' [ %s ] %-16s %s\n' "$1" "$2" "$3"; }   # 标签用 ASCII, 中文只放在值里

    line "OK" "bash" "$BASH_VERSION"

    if command -v dpkg-deb >/dev/null 2>&1; then
        DPV=$(dpkg-deb --version 2>/dev/null | head -1 | grep -oE '[0-9]+\.[0-9]+(\.[0-9]+)?' | head -1)
        line "OK" "dpkg-deb" "${DPV:-已安装}"
        if has_out dpkg-deb --help root-owner-group; then
            line "OK" "owner" "支持 --root-owner-group, 包内属主可强制 root:root(无需 sudo)"
        else
            line "!!" "owner" "版本过旧, 无 --root-owner-group, 请用 sudo 打包"
        fi
    else
        line "XX" "dpkg-deb" "缺失, 没有它无法生成 .deb"
        MISS=1
    fi

    MISSING=""
    for c in cp find du md5sum sed awk chmod mkdir mktemp; do
        command -v "$c" >/dev/null 2>&1 || MISSING="$MISSING $c"
    done
    if [ -n "$MISSING" ]; then
        line "XX" "coreutils" "缺失:$MISSING"
        MISS=1
    else
        line "OK" "coreutils" "cp find du md5sum sed awk chmod mkdir 齐全"
    fi

    # 压缩后端: 真打一个 1KB 的空包试, 比查文档可靠(压缩库缺失时才失败)
    TRY=$(mktemp -d 2>/dev/null || true)
    if [ -n "$TRY" ]; then
        mkdir -p "$TRY/p/DEBIAN"
        printf 'Package: t\nVersion: 1\nArchitecture: all\nMaintainer: t <t@t>\nDescription: t\n' \
            >"$TRY/p/DEBIAN/control"
        AVAIL=""
        for z in gzip xz zstd; do
            if dpkg-deb -Z"$z" -z1 --build "$TRY/p" "$TRY/o.deb" >/dev/null 2>&1; then
                AVAIL="$AVAIL $z"
            fi
            rm -f "$TRY/o.deb"
        done
        rm -rf "$TRY"
        if [ -n "${AVAIL# }" ]; then
            case " $AVAIL " in
                *" zstd "*) line "OK" "compress" "dpkg 可用:${AVAIL} (默认 auto: 优先多线程 pigz)" ;;
                *)          line "OK" "compress" "dpkg 可用:${AVAIL} (无 zstd, 不影响)" ;;
            esac
        else
            line "XX" "compress" "gzip/xz/zstd 全部不可用, 无法压缩"
            MISS=1
        fi
    fi

    # pigz: 多线程 gzip, 走自建流程(tar|pigz + ar), 11GB 大包能快 9 倍
    if pigz_ok; then
        PV=$(pigz --version 2>&1 | head -1 | grep -oE '[0-9]+\.[0-9]+(\.[0-9]+)?' | head -1)
        line "OK" "pigz" "${PV:-已安装} + ar (多线程压缩, 大包约快 9 倍)"
    else
        line "!!" "pigz" "缺 pigz 或 ar, 只能用单线程; sudo apt install pigz 可明显加速"
    fi

    if command -v convert >/dev/null 2>&1; then
        CV=$(convert -version 2>/dev/null | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+(-[0-9]+)?' | head -1)
        line "OK" "imagemagick" "${CV:-已安装} (可生成多尺寸/透明图标)"
    elif command -v magick >/dev/null 2>&1; then
        line "!!" "imagemagick" "只找到 magick(IM7), 工具用的是 convert, 图标会被跳过"
    else
        line "!!" "imagemagick" "缺失, 桌面图标将用默认图案"
    fi

    DISKPROBE="$WORKROOT"
    [ -d "$DISKPROBE" ] || DISKPROBE="$DEBKIT_DIR"
    ROOM=$(df -h "$DISKPROBE" 2>/dev/null | awk 'NR==2{print $4}')
    line "OK" "disk" "暂存目录所在分区可用 ${ROOM:-未知} (建议 >= 源体积的 2 倍)"

    echo
    if [ "$MISS" = 1 ]; then
        warn "缺少必需组件, 请先安装:"
        echo "  Debian / Ubuntu / 统信 / 麒麟:  sudo apt install dpkg coreutils"
        echo "  RHEL / CentOS:              sudo yum install dpkg"
        exit 1
    fi
    ok "环境满足打包要求"
    echo "  可选: sudo apt install imagemagick   # 生成多尺寸图标, 桌面显示更清晰"
    exit 0
fi

# ---------------------------------------------------------------------------
# 交互式向导 (没有任何参数且在终端里运行时自动进入)
# ---------------------------------------------------------------------------
if [ "$INTERACTIVE" = 0 ] && [ "$ARGC" -eq 0 ] && [ -t 0 ] && [ -t 1 ]; then
    INTERACTIVE=1
fi
if [ "$INTERACTIVE" = 1 ]; then
    run_wizard
fi

# ---------------------------------------------------------------------------
# 读取配置 (先给默认值, 再用配置文件覆盖)
# ---------------------------------------------------------------------------
APP_TITLE=""; PKG_NAME=""; VERSION="1.0.0"; REVISION="1"
MAINTAINER="unknown <root@localhost>"; HOMEPAGE=""
SECTION="misc"; PRIORITY="optional"; ARCH="amd64"
DESC_SHORT=""; DESC_LONG=""
SRC_DIR=""; EXECUTABLE=""; LOGO=""; INSTALL_DIR=""
CATEGORIES="Utility;"; GENERIC_NAME=""; KEYWORDS=""; EXTRA_ARGS=""
AUTOSTART_DEFAULT="yes"; AUTOSTART_DELAY="3"; CREATE_DESKTOP_ICON="yes"
# 默认画质档位索引: 装包时写 /etc/<包名>/quality, 启动器读到就补 -picture-quality <索引>。
# 只接受数字(0..QualitySettings 档位数-1), 留 none = 不写文件, 由程序自己决定。
QUALITY_DEFAULT="none"
# 默认图形接口: 装包时写 /etc/<包名>/graphics, opengl|vulkan 时启动器补 -force-glcore / -force-vulkan。
# 默认 none = 不指定, 由 Unity 自己挑接口。
GRAPHICS_DEFAULT="none"
WRITABLE_DIRS=""
COMPRESS="gzip"; COMPRESS_LEVEL="6"
DEPENDS="libc6, libstdc++6"
EXCLUDE=""

[ -f "$CONFIG" ] || die "配置文件不存在: $CONFIG
用 -l 查看可用配置, 或参考 appConfigs/example.conf 新建一个"
# shellcheck disable=SC1090
source "$CONFIG"

[ -n "$CLI_VERSION" ]  && VERSION="$CLI_VERSION"
[ -n "$CLI_REVISION" ] && REVISION="$CLI_REVISION"
[ -n "$CLI_SRC" ]      && SRC_DIR="$CLI_SRC"
[ -n "$CLI_COMPRESS" ] && COMPRESS="$CLI_COMPRESS"
[ -n "$CLI_LEVEL" ]    && COMPRESS_LEVEL="$CLI_LEVEL"
[ -n "$CLI_AUTOSTART" ] && AUTOSTART_DEFAULT="$CLI_AUTOSTART"

# ---------------------------------------------------------------------------
# 校验
# ---------------------------------------------------------------------------
step "校验配置"
printf '  配置:      %s\n' "$CONFIG"
printf '  程序名:    %s\n' "${APP_TITLE:-<未设置>}"
printf '  包名:      %s\n' "${PKG_NAME:-<未设置>}"
printf '  版本:      %s-%s\n' "$VERSION" "$REVISION"
printf '  源码目录:  %s\n' "${SRC_DIR:-<未设置>}"
printf '  安装路径:  %s\n' "${INSTALL_DIR:-<未设置>}"
[ -n "$APP_TITLE" ] || die "APP_TITLE 未设置"
[ -n "$PKG_NAME" ]  || die "PKG_NAME 未设置"
validate_pkg_name "$PKG_NAME"
validate_version "$VERSION"
[ -n "$SRC_DIR" ]     || die "SRC_DIR 未设置"
[ -d "$SRC_DIR" ]     || die "源码目录不存在: $SRC_DIR"
[ -n "$EXECUTABLE" ]  || die "EXECUTABLE 未设置"
[ -x "$SRC_DIR/$EXECUTABLE" ] || [ -f "$SRC_DIR/$EXECUTABLE" ] \
    || die "可执行文件不存在: $SRC_DIR/$EXECUTABLE"
[ -n "$INSTALL_DIR" ] || INSTALL_DIR="/opt/$PKG_NAME"
case "$INSTALL_DIR" in /*) : ;; *) die "INSTALL_DIR 必须是绝对路径" ;; esac

# dpkg-deb 只在真正要产出 .deb 时才必需: --no-build 与 -Z none 都只到安装树为止,
# 这样在 WSL 之外的纯 Windows Git Bash 里也能先把树生成出来看结构。
if [ "$SKIP_BUILD" = 0 ] && [ "$(printf '%s' "${CLI_COMPRESS:-$COMPRESS}" | tr 'A-Z' 'a-z')" != none ]; then
    need_cmd dpkg-deb
fi
need_cmd cp find du md5sum sed awk

# logo 解析: 支持绝对路径或相对源码目录
if [ -n "$LOGO" ]; then
    case "$LOGO" in
        /*) LOGO_PATH="$LOGO" ;;
        *)  LOGO_PATH="$SRC_DIR/$LOGO" ;;
    esac
    [ -f "$LOGO_PATH" ] || { warn "logo 不存在: $LOGO_PATH (将使用默认图标)"; LOGO_PATH=""; }
else
    LOGO_PATH=""
fi

case "$AUTOSTART_DEFAULT" in
    yes|YES|true|1|on)  AUTOSTART_HIDDEN="false"; AUTOSTART_ENABLED="true" ;;
    no|NO|false|0|off)  AUTOSTART_HIDDEN="true";  AUTOSTART_ENABLED="false" ;;
    *) die "AUTOSTART_DEFAULT 只能是 yes 或 no" ;;
esac
case "$AUTOSTART_DELAY" in ''|*[!0-9]*) AUTOSTART_DELAY=3 ;; esac

# 默认画质: 只接受 QualitySettings 的档位索引(数字)。
# 写 none(或留空) 时 QUALITY_DEFAULT_NAME 为空 = 不生成 quality 文件, 由程序自己决定。
case "$(printf '%s' "${QUALITY_DEFAULT:-}" | tr 'A-Z' 'a-z')" in
    ""|none|no|off|unset)   QUALITY_DEFAULT_NAME="" ;;
    *[!0-9]*)               die "QUALITY_DEFAULT 只接受数字索引(0 起), 或 none: $QUALITY_DEFAULT" ;;
    *)                      QUALITY_DEFAULT_NAME="$QUALITY_DEFAULT" ;;
esac

# 默认图形接口: 落成 opengl / vulkan / 空(不写文件, 由 Unity 自己挑)。
case "$(printf '%s' "${GRAPHICS_DEFAULT:-}" | tr 'A-Z' 'a-z')" in
    ""|none|auto|unset)     GRAPHICS_DEFAULT_NAME="" ;;
    opengl|gl|glcore)       GRAPHICS_DEFAULT_NAME="opengl" ;;
    vulkan|vk)              GRAPHICS_DEFAULT_NAME="vulkan" ;;
    *) die "GRAPHICS_DEFAULT 只能是 opengl / vulkan (或 none): $GRAPHICS_DEFAULT" ;;
esac

# 程序固定以登录用户运行。需要往安装目录写(配置 / StreamingAssets / 录像)时,
# 把这些子路径在装包时交给运行用户, 这样"能写"不必等于"跑 root"。
# 必须是相对 INSTALL_DIR 的路径, 空格分隔; 绝对路径和 .. 一律拒绝 ——
# postinst 是 root 执行的, 放开了等于让配置文件 chown 任意系统路径。
if [ -n "$WRITABLE_DIRS" ]; then
    _wd=""
    for _d in $WRITABLE_DIRS; do
        case "$_d" in
            /*)   die "WRITABLE_DIRS 只能是相对 INSTALL_DIR 的路径: $_d" ;;
            *..*) die "WRITABLE_DIRS 不允许包含 ..: $_d" ;;
        esac
        _d="${_d#./}"
        _d="${_d%/}"
        [ -n "$_d" ] || continue
        _wd="$_wd $_d"
        [ -e "$SRC_DIR/$_d" ] || warn "WRITABLE_DIRS 里的 $_d 在源目录中不存在, 装包时会新建空目录"
    done
    WRITABLE_DIRS="${_wd# }"
fi
case "$COMPRESS" in
    xz|gzip|zstd|pigz|none) : ;;
    auto|"") COMPRESS="auto" ;;   # 见下方 comp_ok(), 按系统支持情况挑
    *) die "不支持的压缩类型: $COMPRESS (可选 pigz/gzip/xz/zstd/none/auto)" ;;
esac
case "$COMPRESS_LEVEL" in ''|*[!0-9]*) COMPRESS_LEVEL=1 ;; esac
[ "$COMPRESS_LEVEL" -gt 9 ] && COMPRESS_LEVEL=9
[ "$COMPRESS_LEVEL" -lt 1 ] && COMPRESS_LEVEL=1
ok "配置校验通过"
if [ -n "$QUALITY_DEFAULT_NAME" ]; then
    printf '  默认画质:  索引 %s (装包时写入 /etc/%s/quality)\n' "$QUALITY_DEFAULT_NAME" "$PKG_NAME"
else
    printf '  默认画质:  由程序自己决定 (未生成 quality 文件)\n'
fi
case "$GRAPHICS_DEFAULT_NAME" in
    opengl) printf '  图形接口:  默认 OpenGL (-force-glcore, 装包时写入 /etc/%s/graphics)\n' "$PKG_NAME" ;;
    vulkan) printf '  图形接口:  默认 Vulkan (-force-vulkan, 装包时写入 /etc/%s/graphics)\n' "$PKG_NAME" ;;
    *)      printf '  图形接口:  由 Unity 自己选择 (未生成 graphics 文件)\n' ;;
esac
printf '  运行身份:  当前登录用户(不提权)\n'
if [ -n "$WRITABLE_DIRS" ]; then
    printf '  可写路径:  %s (装包时属主改为运行用户)\n' "$WRITABLE_DIRS"
fi

# ---------------------------------------------------------------------------
# 目录准备
# ---------------------------------------------------------------------------
FULL_VER="$VERSION-$REVISION"
STAGE="$WORKROOT/$PKG_NAME-$FULL_VER"
ROOT="$STAGE/root"
TMP_DIR="$WORKROOT/.tmp-$PKG_NAME"
DEB_NAME="${PKG_NAME}_${VERSION}_${ARCH}.deb"
# 共享目录常常是 root:root, 普通用户不可写 —— 自动退到自己的家目录
if ! { mkdir -p "$WORKROOT" 2>/dev/null && [ -w "$WORKROOT" ]; }; then
    ALT="${XDG_DATA_HOME:-$HOME/.local/share}/debkit/work"
    mkdir -p "$ALT" || die "工作目录不可写, 且无法创建 $ALT"
    warn "不可写: $WORKROOT"
    warn "改用它: $ALT  (可用 -w 或被 DEBKIT_WORK 指定)"
    WORKROOT="$ALT"
    STAGE="$WORKROOT/$PKG_NAME-$FULL_VER"
    ROOT="$STAGE/root"
    TMP_DIR="$WORKROOT/.tmp-$PKG_NAME"
fi
if ! { mkdir -p "$OUTDIR" 2>/dev/null && [ -w "$OUTDIR" ]; }; then
    ALT="${XDG_DATA_HOME:-$HOME/.local/share}/debkit/output"
    mkdir -p "$ALT" || die "输出目录不可写, 且无法创建 $ALT"
    warn "不可写: $OUTDIR"
    warn "改用它: $ALT  (可用 -o 或被 DEBKIT_OUT 指定)"
    OUTDIR="$ALT"
fi
DEB_OUT="$OUTDIR/$DEB_NAME"

cleanup() {
    rm -rf "$TMP_DIR"
    if [ "$KEEP_STAGE" = 1 ]; then
        warn "已保留暂存目录: $STAGE"
    else
        rm -rf "$STAGE"
        # 暂存根目录也撤掉(-w 指到 WSL 原生盘时尤其重要: 一份源+安装树+data.tar.gz 峰值约 30GB)。
        # rmdir 只删空目录, 别人放进去的东西不受影响。
        rmdir "$WORKROOT" 2>/dev/null || true
    fi
}
trap cleanup EXIT

step "准备打包目录"
rm -rf "$STAGE" "$TMP_DIR"
mkdir -p "$ROOT/DEBIAN" "$TMP_DIR"
# 本机 /tmp 可能很小(tmpfs), 让 dpkg-deb 用工作目录做临时空间
export TMPDIR="$TMP_DIR"
ok "暂存目录: $STAGE"

# ---------------------------------------------------------------------------
# 复制程序文件
# ---------------------------------------------------------------------------
step "复制程序文件"
SRC_BYTES=$(du -sb "$SRC_DIR" 2>/dev/null | awk '{print $1}')
printf '  源大小: %s\n' "$(human_size "${SRC_BYTES:-0}")"
DEST_DIR="$ROOT${INSTALL_DIR}"
mkdir -p "$DEST_DIR"
# 不保留源属主: 普通用户 cp -a 会逐个尝试 chown 并大量报错,
# 属主最终由 dpkg-deb --root-owner-group 统一成 root:root
pb_copy "$SRC_DIR" "$DEST_DIR" "${SRC_BYTES:-0}" || die "复制失败: $SRC_DIR"

# 排除规则(空格分隔的 shell 通配)
if [ -n "$EXCLUDE" ]; then
    for pat in $EXCLUDE; do
        find "$DEST_DIR" -name "$pat" -prune -exec rm -rf {} + 2>/dev/null || true
    done
    ok "已应用排除规则: $EXCLUDE"
fi

# logo 不随程序发布
# 注意写成 if 而不是 `[ -n ... ] && rm ...`: 后者在 rm 失败(权限/安全策略)时
# 会成为 && 链的最后一个命令, 触发 set -e 静默退出, 连错误提示都没有
if [ -n "$LOGO_PATH" ]; then
    rm -f "$DEST_DIR/$(basename "$LOGO_PATH")" || warn "未能移除源目录里的 logo 副本(不影响打包)"
fi

# ---------------------------------------------------------------------------
# 权限归一化 (源目录常见 666/777, 装到系统里要规范)
# ---------------------------------------------------------------------------
step "归一化权限"
# 属主只有 root 能改。非 root 打包时跳过, 包内属主由 --root-owner-group 保证。
if [ "$(id -u)" = 0 ]; then
    chown -R 0:0 "$ROOT" 2>/dev/null || warn "部分文件属主未能修改 (不影响安装, 包内属主会被强制为 root)"
    ok "属主 root:root"
else
    printf '  %s以普通用户运行: 跳过 chown, 包内属主由 --root-owner-group 统一为 root:root%s\n' \
        "${C_DIM:-}" "${C_RESET:-}"
fi
find "$ROOT" -type d -exec chmod 755 {} +
find "$ROOT" -type f -exec chmod 644 {} +
chmod 755 "$DEST_DIR/$EXECUTABLE"
find "$DEST_DIR" -type f \( -name '*.so' -o -name '*.so.*' -o -name '*.sh' \) -exec chmod 755 {} +
ok "目录 755 / 文件 644 / 可执行与动态库 755"

# ---------------------------------------------------------------------------
# 图标
# ---------------------------------------------------------------------------
step "生成图标"
ICON_TMP="$STAGE/icons"
mkdir -p "$ICON_TMP"
if [ -n "$LOGO_PATH" ]; then
    make_icons "$LOGO_PATH" "$ICON_TMP" "$PKG_NAME" || warn "图标生成失败, 桌面将显示默认图标"
    if [ -d "$ICON_TMP/icons" ]; then
        mkdir -p "$ROOT/usr/share/icons/hicolor" "$ROOT/usr/share/pixmaps"
        cp -a "$ICON_TMP/icons/." "$ROOT/usr/share/icons/hicolor/"
        [ -f "$ICON_TMP/pixmap/$PKG_NAME.png" ] && \
            cp -f "$ICON_TMP/pixmap/$PKG_NAME.png" "$ROOT/usr/share/pixmaps/$PKG_NAME.png"
        # 同权限归一化: 非 root 时跳过属主, 只调权限
        if [ "$(id -u)" = 0 ]; then
            chown -R 0:0 "$ROOT/usr/share/icons" "$ROOT/usr/share/pixmaps" 2>/dev/null || true
        fi
        find "$ROOT/usr/share/icons" "$ROOT/usr/share/pixmaps" -type d -exec chmod 755 {} +
        find "$ROOT/usr/share/icons" "$ROOT/usr/share/pixmaps" -type f -exec chmod 644 {} +
        ok "图标: /usr/share/icons/hicolor/*/apps/$PKG_NAME.png"
    fi
else
    warn "未提供 logo, 跳过"
fi

# ---------------------------------------------------------------------------
# 桌面集成: 菜单项 / 自启动 / 启动器 / 控制工具
# ---------------------------------------------------------------------------
step "生成桌面集成文件"
# EXTRA_ARGS 会写进启动器的双引号字符串里, 先做一遍 shell 转义,
# 这样参数里出现 $ " ` \ 也不会被 sh 解析成变量或提前截断。
EXTRA_ARGS_SH=$(printf '%s' "$EXTRA_ARGS" | sed -e 's/[\\"$`]/\\&/g')
COMMON_VARS=(
    "PKG_NAME=$PKG_NAME"
    "APP_TITLE=$APP_TITLE"
    "GENERIC_NAME=${GENERIC_NAME:-$APP_TITLE}"
    "DESC_SHORT=${DESC_SHORT:-$APP_TITLE}"
    "CATEGORIES=$CATEGORIES"
    "KEYWORDS=$KEYWORDS"
    "EXTRA_ARGS=$EXTRA_ARGS_SH"
    "INSTALL_DIR=$INSTALL_DIR"
    "EXECUTABLE=$EXECUTABLE"
    "AUTOSTART_DELAY=$AUTOSTART_DELAY"
    "QUALITY_DEFAULT=$QUALITY_DEFAULT_NAME"
    "GRAPHICS_DEFAULT=$GRAPHICS_DEFAULT_NAME"
    "CREATE_DESKTOP_ICON=${CREATE_DESKTOP_ICON:-yes}"
    "WRITABLE_DIRS=$WRITABLE_DIRS"
)

mkdir -p "$ROOT/usr/bin" "$ROOT/usr/share/applications" \
         "$ROOT/usr/lib/$PKG_NAME" "$ROOT/etc/xdg/autostart"

# 启动器
render_exec "$DEBKIT_DIR/templates/launcher.sh.in" "$ROOT/usr/bin/$PKG_NAME" "${COMMON_VARS[@]}"
# 控制工具
render_exec "$DEBKIT_DIR/templates/appctl.sh.in" "$ROOT/usr/bin/$PKG_NAME-ctl" "${COMMON_VARS[@]}"
# 桌面快捷方式批量维护(install/prerm 用)
render_exec "$DEBKIT_DIR/templates/user-setup.sh.in" "$ROOT/usr/lib/$PKG_NAME/user-setup.sh" "${COMMON_VARS[@]}"

SCRIPT_CHECK=("$ROOT/usr/bin/$PKG_NAME" "$ROOT/usr/bin/$PKG_NAME-ctl"
              "$ROOT/usr/lib/$PKG_NAME/user-setup.sh")

# 开始菜单项
render "$DEBKIT_DIR/templates/app.desktop.in" \
       "$ROOT/usr/share/applications/$PKG_NAME.desktop" "${COMMON_VARS[@]}"
chmod 644 "$ROOT/usr/share/applications/$PKG_NAME.desktop"

# 自启动设置入口(菜单里点一下切换开关)
render "$DEBKIT_DIR/templates/autostart-toggle.desktop.in" \
       "$ROOT/usr/share/applications/$PKG_NAME-autostart.desktop" "${COMMON_VARS[@]}"
chmod 644 "$ROOT/usr/share/applications/$PKG_NAME-autostart.desktop"

# 系统级自启动项
render "$DEBKIT_DIR/templates/autostart.desktop.in" \
       "$ROOT/etc/xdg/autostart/$PKG_NAME.desktop" "${COMMON_VARS[@]}" \
       "AUTOSTART_HIDDEN=$AUTOSTART_HIDDEN" "AUTOSTART_ENABLED=$AUTOSTART_ENABLED"
chmod 644 "$ROOT/etc/xdg/autostart/$PKG_NAME.desktop"

# 去掉 Exec 行末尾因空参数留下的空格
for d in "$ROOT/usr/share/applications/$PKG_NAME.desktop" \
         "$ROOT/usr/share/applications/$PKG_NAME-autostart.desktop" \
         "$ROOT/etc/xdg/autostart/$PKG_NAME.desktop"; do
    sed -i 's/[[:space:]]\+$//' "$d"
done

# 语法自检(用重定向读取, 兼容部分安全策略不允许直接执行目录内脚本的系统)
for s in "${SCRIPT_CHECK[@]}"; do
    sh -n <"$s" || die "脚本语法错误: $s"
done
ok "启动器 / 控制工具 / 菜单项 / 自启动项 已生成"
printf '  开机自启默认: %s (延迟 %ss)\n' "$( [ "$AUTOSTART_HIDDEN" = false ] && echo 开启 || echo 关闭 )" "$AUTOSTART_DELAY"

# 这一步又用 mkdir 新建了 usr/ etc/ 等目录, 在 umask 002 下会变成 775,
# 打包前统一回 755, 免得装到系统里留下组可写目录
find "$ROOT" \( -path "$ROOT/DEBIAN" -prune \) -o -type d -exec chmod 755 {} +

# ---------------------------------------------------------------------------
# DEBIAN 控制文件
# ---------------------------------------------------------------------------
step "生成 DEBIAN 元数据"

# Depends 可能为空
if [ -n "$DEPENDS" ]; then
    DEPENDS_LINE="Depends: $DEPENDS"
else
    DEPENDS_LINE=""
fi

INSTALLED_KB=$(du -sk --exclude=DEBIAN "$ROOT" 2>/dev/null | awk '{print $1}')
{
    echo "Package: $PKG_NAME"
    echo "Version: $FULL_VER"
    echo "Section: $SECTION"
    echo "Priority: $PRIORITY"
    echo "Architecture: $ARCH"
    echo "Installed-Size: $INSTALLED_KB"
    echo "Maintainer: $MAINTAINER"
    [ -n "$HOMEPAGE" ] && echo "Homepage: $HOMEPAGE"
    [ -n "$DEPENDS_LINE" ] && echo "$DEPENDS_LINE"
    echo "Description: ${DESC_SHORT:-$APP_TITLE}"
    if [ -n "$DESC_LONG" ]; then
        printf '%s\n' "$DESC_LONG" | while IFS= read -r line; do
            if [ -z "$line" ]; then
                echo " ."
            elif [ "${line# }" != "$line" ]; then
                echo "$line"
            else
                echo " $line"
            fi
        done
    fi
} >"$ROOT/DEBIAN/control"
chmod 644 "$ROOT/DEBIAN/control"

# 维护脚本
for s in postinst prerm postrm; do
    render_exec "$DEBKIT_DIR/templates/$s.in" "$ROOT/DEBIAN/$s" "${COMMON_VARS[@]}"
    sh -n <"$ROOT/DEBIAN/$s" || die "维护脚本语法错误: $s"
done

ok "control / postinst / prerm / postrm 就绪"

# ---------------------------------------------------------------------------
# md5sums
# ---------------------------------------------------------------------------
step "计算校验和 (文件较多时稍慢)"
MD5_N=$(find "$ROOT" -path "$ROOT/DEBIAN" -prune -o -type f -print 2>/dev/null | wc -l)
printf '  待校验: %s 个文件\n' "$MD5_N"
pb_md5 "$ROOT" "$ROOT/DEBIAN/md5sums" "$MD5_N" || warn "md5sums 生成失败(不影响安装)"
chmod 644 "$ROOT/DEBIAN/md5sums"
ok "md5sums: $(wc -l <"$ROOT/DEBIAN/md5sums") 个文件"

# ---------------------------------------------------------------------------
# 打包
# ---------------------------------------------------------------------------
if [ "$SKIP_BUILD" = 1 ]; then
    warn "--no-build 已指定, 停在安装树: $ROOT"
    KEEP_STAGE=1
    exit 0
fi

[ -f "$DEB_OUT" ] && rm -f "$DEB_OUT"
BUILD_ARGS=(--build "$ROOT" "$DEB_OUT")
if has_out dpkg-deb --help root-owner-group; then
    BUILD_ARGS=(--root-owner-group "${BUILD_ARGS[@]}")
elif [ "$(id -u)" != 0 ]; then
    warn "当前 dpkg-deb 不支持 --root-owner-group, 且未以 root 运行"
    warn "包内文件属主会是 $(id -un), 安装后可能异常; 建议用 sudo 打包或升级 dpkg"
fi

# zstd 是较新 dpkg 才有的(-Zzstd 走 libzstd), 老系统上会直接失败。
# 打包前先拿一个 1KB 空包试一下, 不行就按顺序降级, 免得白复制 11GB。
comp_ok() {
    local z="$1" t rc=0
    [ "$z" = none ] && return 0
    [ "$z" = pigz ] && { pigz_ok; return; }
    t=$(mktemp -d 2>/dev/null) || return 0
    mkdir -p "$t/p/DEBIAN"
    printf 'Package: t\nVersion: 1\nArchitecture: all\nMaintainer: t <t@t>\nDescription: t\n' \
        >"$t/p/DEBIAN/control"
    dpkg-deb -Z"$z" -z1 --build "$t/p" "$t/o.deb" >/dev/null 2>&1 || rc=1
    rm -rf "$t"
    return $rc
}

# pigz 走自建流程(tar|pigz + ar), 能力探测见 lib/common.sh 的 pigz_ok()

if [ "$COMPRESS" = auto ]; then
    # pigz 优先: 输出仍是标准 gzip 流(兼容性同 gzip), 但能吃满多核
    for z in pigz zstd gzip xz; do
        if comp_ok "$z"; then COMPRESS="$z"; break; fi
    done
    [ "$COMPRESS" = auto ] && die "pigz/zstd/gzip/xz 全部不可用, 无法压缩"
    ok "自动选择压缩方式: $COMPRESS"
elif ! comp_ok "$COMPRESS"; then
    warn "本机用不了 $COMPRESS (dpkg 版本过旧, 或缺少 pigz/ar), 自动降级"
    for z in pigz gzip xz; do
        if comp_ok "$z"; then COMPRESS="$z"; break; fi
    done
    ok "改用: $COMPRESS"
fi

# 横幅放在算法解析之后: Unity 侧是拿 "==>" 行当进度条标题的, 提前打会把 "auto" 一路显示下去,
# 看上去像还没开始压 (实际早在跑 pigz 了)。
step "压缩打包 ($COMPRESS -$COMPRESS_LEVEL)"

# data.tar 要读的字节数 = 所有普通文件大小之和, 拿来算压缩进度
DATA_BYTES=$(find "$ROOT" -path "$ROOT/DEBIAN" -prune -o -type f -printf '%s\n' 2>/dev/null \
    | awk '{s += $1} END {printf "%.0f\n", s}')
printf '  待压缩: %s (%s)\n' "$(human_size "${DATA_BYTES:-0}")" "$COMPRESS -$COMPRESS_LEVEL"
if [ "${DATA_BYTES:-0}" -gt $((1024 * 1024 * 1024)) ] && [ "$COMPRESS" != pigz ]; then
    printf '  %s单线程压缩, 11GB 级别要几分钟; 进度条会实时走, Ctrl-C 可中断%s\n' \
        "${C_DIM:-}" "${C_RESET:-}"
fi

# 自建流程: tar | pigz 分别打包 control / data, 再用 ar 拼成 .deb。
# deb 就是普通 ar 归档, 结构等价于 dpkg-deb 产物; 好处是 pigz 能吃满多核,
# 11 GB 的工程从 7 分钟降到 1 分钟级。输出仍是标准 gzip 流, 兼容性不变。
build_pigz() {
    local tmpd="$TMP_DIR/ar"
    rm -rf "$tmpd"
    mkdir -p "$tmpd"
    printf '2.0\n' >"$tmpd/debian-binary"

    ( cd "$ROOT/DEBIAN" && tar --format=gnu --sort=name \
        --owner=root:0 --group=root:0 -cf - . ) \
        | pigz -"$COMPRESS_LEVEL" >"$tmpd/control.tar.gz" || return 1

    ( cd "$ROOT" && tar --format=gnu --sort=name --owner=root:0 --group=root:0 \
        --exclude='./DEBIAN' --exclude='./DEBIAN/*' -cf - . ) \
        | pigz -"$COMPRESS_LEVEL" >"$tmpd/data.tar.gz" || return 1

    ( cd "$tmpd" && ar rD "$DEB_OUT" debian-binary control.tar.gz data.tar.gz ) || return 1
    return 0
}

T0=$(date +%s)
if [ "$COMPRESS" = pigz ]; then
    need_cmd pigz ar tar
    pb_track "${DATA_BYTES:-0}" build_pigz || die "打包失败 (pigz 流程)"
else
    pb_dpkgdeb "${DATA_BYTES:-0}" -Z"$COMPRESS" -z"$COMPRESS_LEVEL" "${BUILD_ARGS[@]}" \
        || die "dpkg-deb 打包失败"
fi
T1=$(date +%s)
[ -f "$DEB_OUT" ] || die "未生成 deb 文件"

# ---------------------------------------------------------------------------
# 校验产物
# ---------------------------------------------------------------------------
step "校验产物"
dpkg-deb -I "$DEB_OUT"
echo

DEB_SIZE=$(stat -c%s "$DEB_OUT")

# 控制脚本是关键, 单独核对(很快)
dpkg-deb -e "$DEB_OUT" "$TMP_DIR/ctl" 2>/dev/null || true
for s in control postinst prerm postrm md5sums; do
    if [ -f "$TMP_DIR/ctl/$s" ]; then
        ok "control.tar 含 $s"
    else
        warn "control.tar 缺少 $s"
    fi
done

# 逐文件清单校验需要把整个 data.tar 解压一遍, 大包很慢, 按体积自动跳过
VERIFY_LIMIT=$((2 * 1024 * 1024 * 1024))
if [ "$VERIFY" = 1 ] || [ "$DEB_SIZE" -lt "$VERIFY_LIMIT" ]; then
    LIST_FILE="$TMP_DIR/.deb-list"
    dpkg-deb -c "$DEB_OUT" >"$LIST_FILE"
    # 一次性抽出路径列, 循环里直接 grep 文件(管道里 grep -q 会被 pipefail 误判)
    awk '{print $6}' "$LIST_FILE" >"$LIST_FILE.paths"
    MUST=( "./usr/bin/$PKG_NAME"
           "./usr/share/applications/$PKG_NAME.desktop"
           "./etc/xdg/autostart/$PKG_NAME.desktop"
           "./usr/share/pixmaps/$PKG_NAME.png"
           "./usr/lib/$PKG_NAME/user-setup.sh"
           ".${INSTALL_DIR}/$EXECUTABLE" )
    for must in "${MUST[@]}"; do
        if grep -qxF "$must" "$LIST_FILE.paths"; then
            ok "包含 $must"
        else
            warn "缺少 $must"
        fi
    done
    printf '  包内文件数: %s\n' "$(wc -l <"$LIST_FILE")"
else
    warn "包体积 $(human_size "$DEB_SIZE") 较大, 跳过逐文件清单校验"
    warn "(全量校验需要完整解压一遍, 很慢; 需要时加 --verify)"
fi
printf '\n'
step "完成"
printf '  包文件:    %s\n' "$DEB_OUT"
printf '  包体积:    %s (源 %s)\n' "$(human_size "$DEB_SIZE")" "$(human_size "${SRC_BYTES:-0}")"
printf '  安装大小:  %s\n' "$(human_size $((INSTALLED_KB * 1024)))"
printf '  压缩方式:  %s -%s\n' "$COMPRESS" "$COMPRESS_LEVEL"
printf '  运行身份:  登录用户(不提权)\n'
if [ -n "$WRITABLE_DIRS" ]; then
    printf '  可写路径:  %s (装包后属主改为运行用户)\n' "$WRITABLE_DIRS"
fi
BUILD_SEC=$((T1 - T0))
[ "$BUILD_SEC" -gt 0 ] || BUILD_SEC=1
printf '  压缩耗时:  %s (平均 %s/s)\n' "$(_pb_hms "$BUILD_SEC")" \
    "$(human_size $(( ${DATA_BYTES:-0} / BUILD_SEC )))"
cat <<EOF

  安装:      sudo dpkg -i "$DEB_NAME"
            sudo apt-get -f install        # 若提示缺依赖
  启动:      $PKG_NAME
  自启动:    $PKG_NAME-ctl autostart on|off|status
  卸载:      sudo dpkg -r $PKG_NAME

EOF
