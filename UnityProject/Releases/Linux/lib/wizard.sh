#!/usr/bin/env bash
# debkit - 交互式向导
# 由 debkit.sh source 后调用 run_wizard; 最终会生成一份 appConfigs/*.conf,
# 后续流程与 `-c 配置文件` 完全一致(单一代码路径)。

# Unity 程序通用依赖(可在生成的配置里再改)
UNITY_DEFAULT_DEPENDS="libc6, libstdc++6, libgl1 | libgl1-mesa-glx, libglu1-mesa | libglu1, libxcursor1, libxrandr2, libxinerama1, libxi6, libx11-6, libasound2 | libasound2t64, libpulse0, libgtk-3-0 | libgtk-3-0t64"

# ---------------------------------------------------------------------------
# 输入小工具
#   结果放在 ASK_REPLY 里(不用 $(...), 这样才能在输入结束时真正中止脚本)
# ---------------------------------------------------------------------------
ASK_REPLY=""

ask() {
    local prompt="$1" def="${2:-}" ans=""
    if [ -n "$def" ]; then
        printf '  %s [%s]: ' "$prompt" "$def" >&2
    else
        printf '  %s: ' "$prompt" >&2
    fi
    if ! IFS= read -r ans; then
        printf '\n' >&2
        die "输入已结束, 已中止"
    fi
    [ -z "$ans" ] && ans="$def"
    ASK_REPLY="$ans"
}

ask_yn() {
    local prompt="$1" def="${2:-y}" tries=0
    while [ "$tries" -lt 3 ]; do
        tries=$((tries + 1))
        ask "$prompt (y/n)" "$def"
        case "$ASK_REPLY" in
            y|Y|yes|YES|是) ASK_REPLY="yes"; return 0 ;;
            n|N|no|NO|否)   ASK_REPLY="no";  return 0 ;;
        esac
        printf '  请输入 y 或 n\n' >&2
    done
    die "输入无效, 已中止"
}

conf_escape() {
    printf '%s' "$1" | sed -e 's/[\\"$`]/\\&/g'
}

is_elf() {
    [ -f "$1" ] || return 1
    [ "$(head -c 4 "$1" 2>/dev/null | od -An -tx1 | tr -d ' \n')" = "7f454c46" ]
}

slugify() {
    printf '%s' "$1" | tr 'A-Z' 'a-z' \
        | sed -e 's/[^a-z0-9+.-]\+/-/g' -e 's/^[-.]*//' -e 's/[-.]*$//'
}

# 目录布局探测: 输出两行 —— 第一行可执行文件, 第二行 logo
detect_layout() {
    local d="$1" e="" l="" f n
    for f in "$d"/*.x86_64 "$d"/*.x86; do
        [ -f "$f" ] && { e="$f"; break; }
    done
    if [ -z "$e" ]; then
        for f in "$d"/*; do
            [ -f "$f" ] || continue
            case "$f" in
                *.so|*.so.*|*.zip|*.tar|*.gz|*.jpg|*.png|*.txt|*.json) continue ;;
            esac
            is_elf "$f" && { e="$f"; break; }
        done
    fi
    for n in logo.jpg logo.png Logo.jpg LOGO.png icon.png icon.jpg; do
        [ -f "$d/$n" ] && { l="$d/$n"; break; }
    done
    if [ -z "$l" ]; then
        for f in "$d"/*.png "$d"/*.jpg "$d"/*.jpeg; do
            [ -f "$f" ] && { l="$f"; break; }
        done
    fi
    printf '%s\n%s\n' "$e" "$l"
}

# ---------------------------------------------------------------------------
# 向导主体
# ---------------------------------------------------------------------------
run_wizard() {
    local cfgdir="$APPS_DIR"
    local f n=0 pick prefill="" det exe_abs logo_abs base slugs out seen="" b
    local -a cfgarr=()

    # 用户目录 + 系统预置都列出来; 同名时用户目录优先(先加入, 后者跳过)
    for f in "$cfgdir"/*.conf "$SYS_APPS_DIR"/*.conf; do
        [ -f "$f" ] || continue
        b=$(basename "$f")
        case " $seen " in *" $b "*) continue ;; esac
        seen="$seen $b"
        cfgarr+=("$f")
    done

    step "交互式打包向导"

    # --- 0. 选择已有配置 or 新建 ------------------------------------------
    if [ "${#cfgarr[@]}" -gt 0 ]; then
        echo "  已有配置:"
        for f in "${cfgarr[@]}"; do
            n=$((n + 1))
            base=$(sed -n 's/^APP_TITLE="\{0,1\}\([^"]*\)"\{0,1\}.*/\1/p' "$f" | head -n1)
            printf '   %d) %-36s %s\n' "$n" "$(basename "$f" .conf)" "$base"
        done
        echo "   0) 新建一份配置"
        ask "选择" "0"
        pick="$ASK_REPLY"
        case "$pick" in ''|*[!0-9]*) pick=0 ;; esac
        if [ "$pick" -gt 0 ] && [ "$pick" -le "${#cfgarr[@]}" ]; then
            prefill="${cfgarr[$((pick - 1))]}"
            ask_yn "直接用「$(basename "$prefill" .conf)」打包" y
            if [ "$ASK_REPLY" = "yes" ]; then
                CONFIG="$prefill"
                ok "使用已有配置: $CONFIG"
                return 0
            fi
            # 以这份配置为默认值继续问
            # shellcheck disable=SC1090
            . "$prefill"
        fi
    fi

    echo
    echo "  直接回车就用方括号里的默认值。"
    echo

    # --- 1. 程序目录 ------------------------------------------------------
    # 默认取 BuildConfig 的 Linux 输出目录(Releases/Linux/build)的同级推导,
    # 推导不出来就留空, 让用户直接填
    _src_hint="${SRC_DIR:-}"
    if [ -z "$_src_hint" ]; then
        _candidate="$(cd "$(dirname "$0")/.." 2>/dev/null && pwd)/build"
        [ -d "$_candidate" ] && _src_hint="$_candidate"
    fi
    while :; do
        ask "程序所在目录" "$_src_hint"
        SRC_DIR="${ASK_REPLY%/}"
        [ -d "$SRC_DIR" ] && break
        printf '  目录不存在: %s\n' "$SRC_DIR" >&2
    done

    # --- 2. 自动探测可执行文件与 logo -------------------------------------
    det=$(detect_layout "$SRC_DIR")
    exe_abs=$(printf '%s' "$det" | sed -n '1p')
    logo_abs=$(printf '%s' "$det" | sed -n '2p')

    if [ -n "$exe_abs" ]; then
        echo "  自动识别到主程序: $(basename "$exe_abs")" >&2
        ask "主可执行文件" "${EXECUTABLE:-$(basename "$exe_abs")}"
    else
        warn "没找到可执行文件, 请手工填写文件名"
        ask "主可执行文件" "${EXECUTABLE:-}"
    fi
    EXECUTABLE="$ASK_REPLY"
    [ -n "$EXECUTABLE" ] || die "必须指定主可执行文件"
    [ -f "$SRC_DIR/$EXECUTABLE" ] || die "文件不存在: $SRC_DIR/$EXECUTABLE"

    if [ -n "$logo_abs" ]; then
        ask "logo 图片(相对程序目录)" "${LOGO:-$(basename "$logo_abs")}"
    else
        ask "logo 图片(可留空)" "${LOGO:-}"
    fi
    LOGO="$ASK_REPLY"
    if [ -n "$LOGO" ] && [ ! -f "$SRC_DIR/$LOGO" ] && [ ! -f "$LOGO" ]; then
        warn "logo 不存在: $LOGO, 将使用默认图标"
        LOGO=""
    fi

    # --- 3. 显示名 / 包名 -------------------------------------------------
    base=$(basename "$SRC_DIR")
    ask "程序显示名(可中文)" "${APP_TITLE:-$base}"
    APP_TITLE="$ASK_REPLY"
    [ -n "$APP_TITLE" ] || die "显示名不能为空"

    slugs=$(slugify "${PKG_NAME:-$base}")
    [ "${#slugs}" -ge 2 ] || slugs="debkit-app"
    while :; do
        ask "包名(小写, 也是启动命令)" "$slugs"
        PKG_NAME="$ASK_REPLY"
        if printf '%s' "$PKG_NAME" | grep -qE '^[a-z0-9][a-z0-9+.-]+$'; then
            break
        fi
        printf '  包名只能用小写字母/数字/加号/减号/点, 至少 2 个字符\n' >&2
        slugs="$PKG_NAME"
    done

    ask "版本号" "${VERSION:-1.0.0}";        VERSION="$ASK_REPLY"
    ask "打包修订号" "${REVISION:-1}";       REVISION="$ASK_REPLY"
    ask "安装到哪个目录" "${INSTALL_DIR:-/opt/$PKG_NAME}"; INSTALL_DIR="$ASK_REPLY"
    ask "维护人(名字 <邮箱>)" "${MAINTAINER:-$APP_TITLE <root@localhost>}"; MAINTAINER="$ASK_REPLY"

    # --- 4. 开机自启 ------------------------------------------------------
    ask_yn "安装后默认开机自启" "${AUTOSTART_DEFAULT:-yes}"
    AUTOSTART_DEFAULT="$ASK_REPLY"
    ask "登录后延迟几秒再启动" "${AUTOSTART_DELAY:-3}"
    AUTOSTART_DELAY="$ASK_REPLY"
    case "$AUTOSTART_DELAY" in ''|*[!0-9]*) AUTOSTART_DELAY=3 ;; esac

    # --- 5. 其它 ----------------------------------------------------------
    ask "默认画质索引(数字 0 起, 同 Unity Quality 列表; none=由程序决定)" "${QUALITY_DEFAULT:-none}"
    case "$ASK_REPLY" in
        ''|*[!0-9]*) QUALITY_DEFAULT="none" ;;
        *)           QUALITY_DEFAULT="$ASK_REPLY" ;;
    esac
    ask "默认图形接口 opengl(强制 OpenGL)/vulkan(强制 Vulkan)/none(由 Unity 自己挑)" "${GRAPHICS_DEFAULT:-none}"
    case "$(printf '%s' "$ASK_REPLY" | tr 'A-Z' 'a-z')" in
        opengl|gl) GRAPHICS_DEFAULT="opengl" ;;
        vulkan|vk) GRAPHICS_DEFAULT="vulkan" ;;
        *)         GRAPHICS_DEFAULT="none" ;;
    esac
    ask "附加启动参数(可留空)" "${EXTRA_ARGS:-}";                  EXTRA_ARGS="$ASK_REPLY"
    ask "菜单分类" "${CATEGORIES:-Education;Science;Simulation;}"; CATEGORIES="$ASK_REPLY"
    ask "压缩方式 auto(有pigz就多线程)/gzip(快)/xz(小)/zstd(需新dpkg)" "${COMPRESS:-auto}"
    case "$ASK_REPLY" in
        xz|zstd|gzip|pigz|none|auto) COMPRESS="$ASK_REPLY" ;;
        *) COMPRESS="auto" ;;
    esac

    # --- 6. 保存配置 ------------------------------------------------------
    ask "配置保存为" "${PKG_NAME}.conf"
    out="$ASK_REPLY"
    case "$out" in *.conf) : ;; *) out="$out.conf" ;; esac
    case "$out" in
        /*) CONFIG="$out" ;;
        *)  CONFIG="$cfgdir/$(basename "$out")" ;;
    esac
    mkdir -p "$(dirname "$CONFIG")"
    if [ -f "$CONFIG" ]; then
        ask_yn "「$(basename "$CONFIG")」已存在, 覆盖" n
        if [ "$ASK_REPLY" != "yes" ]; then
            CONFIG="$cfgdir/$(basename "${CONFIG%.conf}")-$(date +%H%M%S).conf"
            warn "改为写入: $CONFIG"
        fi
    fi

    {
        printf '# 由 debkit 交互式向导生成: %s\n' "$(date '+%F %T')"
        printf 'APP_TITLE="%s"\n'         "$(conf_escape "$APP_TITLE")"
        printf 'PKG_NAME="%s"\n'          "$(conf_escape "$PKG_NAME")"
        printf 'VERSION="%s"\n'           "$(conf_escape "$VERSION")"
        printf 'REVISION="%s"\n'          "$(conf_escape "$REVISION")"
        printf 'MAINTAINER="%s"\n'        "$(conf_escape "$MAINTAINER")"
        printf 'HOMEPAGE=""\n'
        printf 'SECTION="%s"\n'           "$(conf_escape "${SECTION:-education}")"
        printf 'PRIORITY="optional"\n'
        printf 'ARCH="%s"\n'              "$(conf_escape "${ARCH:-amd64}")"
        printf 'DESC_SHORT="%s"\n'        "$(conf_escape "${DESC_SHORT:-$APP_TITLE}")"
        printf 'DESC_LONG="%s"\n'         "$(conf_escape "${DESC_LONG:-$APP_TITLE}")"
        printf 'SRC_DIR="%s"\n'           "$(conf_escape "$SRC_DIR")"
        printf 'EXECUTABLE="%s"\n'        "$(conf_escape "$EXECUTABLE")"
        printf 'LOGO="%s"\n'              "$(conf_escape "$LOGO")"
        printf 'INSTALL_DIR="%s"\n'       "$(conf_escape "$INSTALL_DIR")"
        printf 'CATEGORIES="%s"\n'        "$(conf_escape "$CATEGORIES")"
        printf 'GENERIC_NAME="%s"\n'      "$(conf_escape "${GENERIC_NAME:-$APP_TITLE}")"
        printf 'KEYWORDS="%s"\n'          "$(conf_escape "${KEYWORDS:-}")"
        printf 'EXTRA_ARGS="%s"\n'        "$(conf_escape "$EXTRA_ARGS")"
        printf 'QUALITY_DEFAULT="%s"\n' "${QUALITY_DEFAULT:-none}"
        printf 'GRAPHICS_DEFAULT="%s"\n' "${GRAPHICS_DEFAULT:-none}"
        printf 'AUTOSTART_DEFAULT="%s"\n' "$AUTOSTART_DEFAULT"
        printf 'AUTOSTART_DELAY="%s"\n'   "$AUTOSTART_DELAY"
        printf 'CREATE_DESKTOP_ICON="%s"\n' "${CREATE_DESKTOP_ICON:-yes}"
        printf 'DEPENDS="%s"\n'           "$(conf_escape "${DEPENDS:-$UNITY_DEFAULT_DEPENDS}")"
        printf 'COMPRESS="%s"\n'          "$COMPRESS"
        printf 'COMPRESS_LEVEL="%s"\n'    "${COMPRESS_LEVEL:-6}"
        printf 'EXCLUDE="%s"\n'           "$(conf_escape "${EXCLUDE:-}")"
    } >"$CONFIG"

    # 立刻回读校验, 有问题早发现
    # shellcheck disable=SC1090
    ( . "$CONFIG" && [ -n "$PKG_NAME" ] ) || die "生成的配置无法解析: $CONFIG"

    ok "配置已保存: $CONFIG"

    echo
    echo "  ------------------------------------------------------------"
    printf '  显示名:    %s\n' "$APP_TITLE"
    printf '  包名:      %s\n' "$PKG_NAME"
    printf '  版本:      %s-%s\n' "$VERSION" "$REVISION"
    printf '  源目录:    %s\n' "$SRC_DIR"
    printf '  主程序:    %s\n' "$EXECUTABLE"
    printf '  安装到:    %s\n' "$INSTALL_DIR"
    printf '  默认画质:  索引 %s\n' "${QUALITY_DEFAULT:-none}"
    printf '  默认接口:  %s\n' "${GRAPHICS_DEFAULT:-none}"
    printf '  开机自启:  %s (延迟 %ss)\n' "$AUTOSTART_DEFAULT" "$AUTOSTART_DELAY"
    printf '  压缩:      %s\n' "$COMPRESS"
    echo "  ------------------------------------------------------------"
    echo

    ask_yn "开始打包" y
    if [ "$ASK_REPLY" != "yes" ]; then
        echo "  已跳过打包。之后可随时执行:"
        echo "    ./debkit.sh -c ${CONFIG#"$DEBKIT_DIR"/}"
        exit 0
    fi
    return 0
}
