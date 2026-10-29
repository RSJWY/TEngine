#!/usr/bin/env bash
# debkit - 公共函数库
# 被 debkit.sh 以 source 方式加载, 不单独执行.

# ---------------------------------------------------------------------------
# 终端颜色
# ---------------------------------------------------------------------------
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  C_RESET=$'\033[0m'; C_RED=$'\033[31m'; C_GRN=$'\033[32m'
  C_YEL=$'\033[33m'; C_BLU=$'\033[36m'; C_BOLD=$'\033[1m'
else
  C_RESET=; C_RED=; C_GRN=; C_YEL=; C_BLU=; C_BOLD=
fi

log()  { printf '%s[%s]%s %s\n' "$C_BLU" "$(date +%H:%M:%S)" "$C_RESET" "$*"; }
step() { printf '\n%s==> %s%s\n' "${C_BOLD}${C_BLU}" "$*" "$C_RESET"; }
ok()   { printf '%s[ OK ]%s %s\n' "$C_GRN" "$C_RESET" "$*"; }
warn() { printf '%s[ !! ]%s %s\n' "$C_YEL" "$C_RESET" "$*" >&2; }
die()  { printf '%s[ XX ]%s %s\n' "$C_RED" "$C_RESET" "$*" >&2; exit 1; }

# ---------------------------------------------------------------------------
# 模板渲染: 把模板里的 @KEY@ 替换成给定值
#   用法: render <模板文件> <输出文件> "KEY1=值1" "KEY2=值2" ...
#   注意: 仅支持单行值; 多行的 Description 由 debkit.sh 里的 gen_control 处理
# ---------------------------------------------------------------------------
render() {
  local tpl="$1" out="$2"; shift 2
  [ -f "$tpl" ] || die "模板不存在: $tpl"

  mkdir -p "$(dirname "$out")"
  cp -f "$tpl" "$out" || die "无法写入: $out"

  local kv key val esc
  for kv in "$@"; do
    key="${kv%%=*}"
    val="${kv#*=}"
    # 转义 sed 替换串里的特殊字符
    esc=$(printf '%s' "$val" | sed -e 's/[\\&|]/\\&/g')
    sed -i "s|@${key}@|${esc}|g" "$out"
  done

  if grep -qE '@[A-Z_]+@' "$out"; then
    warn "模板 $tpl 中仍有未替换的占位符: $(grep -oE '@[A-Z_]+@' "$out" | sort -u | tr '\n' ' ')"
  fi
  return 0
}

# 生成一个可执行脚本文件 (root:root 755)
render_exec() {
  local tpl="$1" out="$2"; shift 2
  render "$tpl" "$out" "$@"
  chmod 755 "$out"
}

# ---------------------------------------------------------------------------
# 校验
# ---------------------------------------------------------------------------
need_cmd() {
  local c
  for c in "$@"; do
    command -v "$c" >/dev/null 2>&1 || die "缺少必需命令: $c"
  done
}

# 包名必须符合 Debian 规则: 小写字母/数字/+-. 且至少 2 个字符, 以字母数字开头
validate_pkg_name() {
  local n="$1"
  case "$n" in
    [a-z0-9]*) : ;;
    *) die "包名必须以小写字母或数字开头: $n" ;;
  esac
  printf '%s' "$n" | grep -qE '^[a-z0-9][a-z0-9+.-]+$' \
    || die "包名只能包含小写字母/数字/加号/减号/点, 且至少 2 字符: $n"
}

# 版本号合法性(宽松): 数字开头, 允许字母数字 . + ~ :
validate_version() {
  printf '%s' "$1" | grep -qE '^[0-9][A-Za-z0-9.+~:-]*$' \
    || die "版本号不合法: $1 (应以数字开头, 只含字母数字和 . + ~ : -)"
}

human_size() {
  numfmt --to=iec --suffix=B "$1" 2>/dev/null || echo "$1 B"
}

# 多线程压缩后端是否可用(pigz 走自建 tar|pigz + ar 流程, 不算 dpkg 的能力)
# 注意: 不要写成 `tar --help | grep -q -- --sort`, grep -q 匹配到就退出,
# tar 收到 SIGPIPE, 在 set -o pipefail 下整条管道会被判成失败。
pigz_ok() {
  command -v pigz >/dev/null 2>&1 || return 1
  command -v ar   >/dev/null 2>&1 || return 1
  local h
  h=$(tar --help 2>/dev/null || true)
  case "$h" in *--sort*) return 0 ;; esac
  return 1
}

# 同上: 在 pipefail 下安全地做"命令输出里有没有某个关键字"
has_out() {  # 命令... | 关键字
  local kw="${!#}" out
  set -- "${@:1:$#-1}"
  out=$("$@" 2>&1 || true)
  case "$out" in *"$kw"*) return 0 ;; esac
  return 1
}

# 目录大小(KB), 用于 Installed-Size
dir_size_kb() {
  du -sk "$1" 2>/dev/null | awk '{print $1}'
}

# 创建桌面快捷方式 / 清理时需要的本地用户列表
list_local_users() {
  awk -F: '$3>=1000 && $3<65534 && $7 !~ /(nologin|false|sync)$/ {print $1":"$3":"$6}' /etc/passwd
}
