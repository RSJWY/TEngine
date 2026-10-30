#!/usr/bin/env bash
# debkit - 构建进度显示
# 由 debkit.sh 以 source 方式加载, 不单独执行。
#
# 为什么要这么绕:
#   dpkg-deb --build 没有 --progress, 它会 fork 一个 tar 把 root/ 打成流,
#   交给压缩器, 压缩结果写进 $TMPDIR/dpkg-deb.XXXXXX。这个临时文件创建后
#   立刻被 unlink(所以 ls 看不见), 但 /proc/<pid>/fd 里仍留着引用, 可以
#   stat 到压缩后的字节数。
#   tar 子进程的 rchar 是「逻辑读取字节」, 走 page cache 也算, 所以拿它除以
#   data.tar 应有的总字节数, 就是不受缓存影响的真实进度。
#
# 三种模式: tty(原地刷新进度条) / line(每 5 秒一行, 给日志用) / off

PB_W=22            # 进度条宽度(字符)
PB_MODE=off        # tty | line | off
PB_INTERVAL=0.4    # 采样间隔(秒)

# 采样函数通过这几个全局变量回传数据
PB_S_LABEL=""; PB_S_DONE=0; PB_S_TOTAL=0; PB_S_NOTE=""; PB_S_UNIT="bytes"

pb_init() {
    PB_MODE=off
    case "${PROGRESS:-auto}" in
        0|no|off|false|False) PB_MODE=off ;;
        tty)                  PB_MODE=tty ;;   # 强制原地刷新(调试/演示用)
        1|yes|on|true)        PB_MODE=$([ -t 1 ] && echo tty || echo line) ;;
        *)  if [ -t 1 ]; then PB_MODE=tty; else PB_MODE=line; fi ;;
    esac
    # 非 UTF-8 终端用 ASCII 字符画条, 免得乱码
    case "${LC_ALL:-}${LC_CTYPE:-}${LANG:-}" in
        *[Uu][Tt][Ff]-8*|*[Uu][Tt][Ff]8*) PB_FULLC='█'; PB_EMPC='░' ;;
        *) PB_FULLC='#'; PB_EMPC='-' ;;
    esac
}

_pb_now_ms() { date +%s%3N 2>/dev/null || echo $(($(date +%s) * 1000)); }

# 强制转成整数: awk/gawk 的默认输出是 %.6g, 大数会变成 1.14528e+10,
# 直接喂给 $(( )) 会报"需要整数表达式"
_pb_int() {
    case "${1:-}" in
        ''|*[!0-9]*) printf '0' ;;
        *) printf '%s' "$1" ;;
    esac
}

# 1234567 -> 1.2M
_pb_size() {
    local b
    b=$(_pb_int "${1:-0}")
    [ "$b" -lt 0 ] && b=0
    if [ "$b" -ge 1073741824 ]; then
        printf '%d.%dG' $((b / 1073741824)) $(((b % 1073741824) * 10 / 1073741824))
    elif [ "$b" -ge 1048576 ]; then
        printf '%d.%dM' $((b / 1048576)) $(((b % 1048576) * 10 / 1048576))
    elif [ "$b" -ge 1024 ]; then
        printf '%dK' $((b / 1024))
    else
        printf '%dB' "$b"
    fi
}

# 3725 -> 01:02:05 / 125 -> 02:05
_pb_hms() {
    local s=${1:-0}
    if [ "$s" -ge 3600 ]; then
        printf '%02d:%02d:%02d' $((s / 3600)) $(((s % 3600) / 60)) $((s % 60))
    else
        printf '%02d:%02d' $((s / 60)) $((s % 60))
    fi
}

# 进程是否还活着(僵尸算已结束, 否则 kill -0 会一直为真)
_pb_alive() {
    local s
    s=$(ps -o stat= -p "$1" 2>/dev/null || true)
    [ -n "$s" ] && [ "${s#Z}" = "$s" ]
}

# 递归收集子孙进程 pid
_pb_descendants() {
    local p="$1" k kids
    kids=$(cat "/proc/$p/task/$p/children" 2>/dev/null || true)
    for k in $kids; do
        printf '%s ' "$k"
        _pb_descendants "$k"
    done
}

# 进程树里最大的普通文件字节数(就是那个被 unlink 的临时包)
_pb_tree_maxfile() {
    local p="$1" f max=0 sz
    for f in /proc/"$p"/fd/*; do
        sz=$(stat -Lc%s "$f" 2>/dev/null || true)
        [ -n "$sz" ] || continue
        [ "$sz" -gt "$max" ] && max=$sz
    done
    printf '%s' "$max"
}

_pb_clear() { [ "$PB_MODE" = tty ] && printf '\r\033[K'; }

# label done total rate el note unit
_pb_draw() {
    local label="$1" done="${2:-0}" total="${3:-0}" rate="${4:-0}" el="${5:-0}"
    local note="${6:-}" unit="${7:-bytes}"
    local p10=0 fill=0 bar="" i line ratestr="" etastr="" left=""

    done=$(_pb_int "$done"); total=$(_pb_int "$total"); rate=$(_pb_int "$rate")
    el=$(_pb_int "$el")

    # 用累计平均速率算 ETA: 瞬时速率每 0.4s 抖一次, 剩余时间会乱跳
    if [ "$el" -ge 2 ] && [ "$done" -gt 0 ]; then
        rate=$((done / el))
    fi

    if [ "$total" -gt 0 ]; then
        p10=$((done * 1000 / total))
        [ "$p10" -gt 999 ] && p10=999
        [ "$p10" -lt 0 ] && p10=0
        fill=$((p10 * PB_W / 1000))
    fi
    for ((i = 0; i < PB_W; i++)); do
        if [ "$i" -lt "$fill" ]; then bar="$bar$PB_FULLC"; else bar="$bar$PB_EMPC"; fi
    done

    if [ "$unit" = count ]; then
        left="$done/$total$note"
    else
        left="$(_pb_size "$done")/$(_pb_size "$total")$note"
    fi

    if [ "$rate" -gt 0 ]; then
        if [ "$unit" = count ]; then
            ratestr="$(($rate))个/s"
        else
            ratestr="$((rate / 1048576))M/s"
        fi
        if [ "$total" -gt "$done" ]; then
            etastr="剩$(_pb_hms $(((total - done) / rate + 1)))"
        fi
    fi

    line=$(printf '%s %d.%d%% [%s] %s' "$label" $((p10 / 10)) $((p10 % 10)) "$bar" "$left")
    [ -n "$ratestr" ] && line="$line $ratestr"
    [ -n "$etastr" ] && line="$line $etastr"
    [ -n "${PB_EXTRA:-}" ] && line="$line $PB_EXTRA"

    case "$PB_MODE" in
        tty)
            printf '\r\033[K  %s  %s' "$line" "$(_pb_hms "$el")"
            ;;
        line)
            # 日志模式: 5 秒一行, 免得刷屏
            local now
            now=$(_pb_now_ms)
            if [ $((now - ${_PB_LAST_LINE:-0})) -ge 5000 ]; then
                _PB_LAST_LINE=$now
                printf '  %s %s  (已用 %s)\n' "$line" "" "$(_pb_hms "$el")"
            fi
            ;;
    esac
}

# 后台跑进程并持续刷新进度; $1=pid, 采样由 PB_SAMPLE_FN 指定的函数完成
_pb_watch() {
    local pid="$1" rc=0 t0 now el rate=0 pdone=0 pms=0
    PB_S_LABEL="处理中"; PB_S_DONE=0; PB_S_NOTE=""; PB_S_UNIT=bytes
    t0=$(_pb_now_ms); pms=$t0
    while _pb_alive "$pid"; do
        sleep "$PB_INTERVAL"
        [ -n "${PB_SAMPLE_FN:-}" ] && "$PB_SAMPLE_FN"
        now=$(_pb_now_ms); el=$(((now - t0) / 1000))
        if [ "$now" -gt "$pms" ]; then
            rate=$(((PB_S_DONE - pdone) * 1000 / (now - pms)))
            [ "$rate" -lt 0 ] && rate=0
        fi
        pdone=$PB_S_DONE; pms=$now
        _pb_draw "$PB_S_LABEL" "$PB_S_DONE" "$PB_S_TOTAL" "$rate" "$el" "$PB_S_NOTE" "$PB_S_UNIT"
    done
    wait "$pid" || rc=$?
    _pb_clear
    return $rc
}

_pb_sum_files() {  # 目录下所有普通文件字节数(排除 DEBIAN)
    find "$1" -path "$1/DEBIAN" -prune -o -type f -printf '%s\n' 2>/dev/null \
        | awk '{s += $1} END {printf "%.0f\n", s}'
}

# ---------------------------------------------------------------------------
# 复制: 靠目标目录体积估计进度
# ---------------------------------------------------------------------------
PB_S_DST=""; PB_S_TOTAL_SRC=0
_pb_s_cp() {
    local d
    d=$(du -sb "$PB_S_DST" 2>/dev/null | awk '{print $1}')
    PB_S_LABEL="复制"; PB_S_DONE=$(_pb_int "${d:-0}")
    PB_S_TOTAL=$PB_S_TOTAL_SRC; PB_S_UNIT=bytes
}

pb_copy() {  # src dst total_bytes
    local src="$1" dst="$2"
    if [ "$PB_MODE" = off ]; then
        cp -a --no-preserve=ownership "$src"/. "$dst"/
        return
    fi
    PB_S_TOTAL_SRC="${3:-0}"
    PB_S_TOTAL=$PB_S_TOTAL_SRC
    PB_S_DST="$dst"
    PB_SAMPLE_FN=_pb_s_cp
    cp -a --no-preserve=ownership "$src"/. "$dst"/ &
    _pb_watch $! || return $?
    PB_SAMPLE_FN=""
    return 0
}

# ---------------------------------------------------------------------------
# md5sum: 按已完成文件数计
# ---------------------------------------------------------------------------
PB_S_OUT=""; PB_S_TOTAL_N=0
_pb_s_md5() {
    local n
    n=$(wc -l <"$PB_S_OUT" 2>/dev/null || echo 0)
    PB_S_LABEL="校验"; PB_S_DONE=${n:-0}
    PB_S_TOTAL=$PB_S_TOTAL_N; PB_S_UNIT=count; PB_S_NOTE=" 个文件"
}

pb_md5() {  # root_dir out_file total_files
    local root="$1" out="$2"
    if [ "$PB_MODE" = off ]; then
        ( cd "$root" && find . -path ./DEBIAN -prune -o -type f -printf '%P\0' \
            | LC_ALL=C sort -z | xargs -0 -r md5sum ) >"$out"
        return
    fi
    PB_S_TOTAL_N="${3:-0}"
    PB_S_TOTAL=$PB_S_TOTAL_N
    PB_S_OUT="$out"
    PB_SAMPLE_FN=_pb_s_md5
    : >"$out"
    ( cd "$root" && find . -path ./DEBIAN -prune -o -type f -printf '%P\0' \
        | LC_ALL=C sort -z | xargs -0 -r md5sum ) >"$out" &
    _pb_watch $! || return $?
    PB_SAMPLE_FN=""
    return 0
}

# ---------------------------------------------------------------------------
# dpkg-deb: tar 的 rchar 算进度, 临时文件体积算吞吐
# ---------------------------------------------------------------------------
PB_D_TOTAL=0
PB_D_DEB=""
PB_D_PID=0
PB_D_TAR=0
PB_D_COMP=0

_pb_s_dpkg() {
    local k comp
    # tar 子进程(读源目录的那个)的 rchar
    PB_D_TAR=0
    for k in $(cat "/proc/$PB_D_PID/task/$PB_D_PID/children" 2>/dev/null); do
        [ "$(cat "/proc/$k/comm" 2>/dev/null || true)" = tar ] && PB_D_TAR=$k
    done
    if [ "$PB_D_TAR" != 0 ]; then
        local r
        r=$(awk '/^rchar:/{print $2}' "/proc/$PB_D_TAR/io" 2>/dev/null || true)
        PB_S_LABEL="压缩"; PB_S_UNIT=bytes; PB_S_TOTAL=$PB_D_TOTAL
        PB_S_DONE=$(_pb_int "${r:-0}")
        PB_S_NOTE=""
        # 压缩吞吐: 临时包体积
        comp=$(_pb_tree_maxfile "$PB_D_PID")
        PB_D_COMP=${comp:-0}
    else
        # tar 结束 = 正在把 control.tar / data.tar 拼进 .deb(此时临时包体积已定)
        local deb
        deb=$(stat -c%s "$PB_D_DEB" 2>/dev/null || echo 0)
        PB_S_LABEL="归档"; PB_S_UNIT=bytes
        PB_S_TOTAL=$((PB_D_COMP + 8192))   # 加上 control.tar 与实际 deb 头, 估个整数
        PB_S_DONE=${deb:-0}
        PB_S_NOTE=""
    fi
}

pb_dpkgdeb() {  # total_bytes dpkg-deb 参数...
    local total="$1"; shift
    local logf="$TMP_DIR/.dpkg-deb.log" rc=0
    if [ "$PB_MODE" = off ]; then
        dpkg-deb "$@" >"$logf" 2>&1
        rc=$?
        [ "$rc" -eq 0 ] || cat "$logf" >&2
        return $rc
    fi
    : >"$logf"
    # dpkg-deb 的"正在构建软件包"是打到 stdout 的, 不接住会冲掉进度行
    dpkg-deb "$@" >"$logf" 2>&1 &
    PB_D_PID=$!
    PB_D_TOTAL="$total"
    PB_D_DEB="$DEB_OUT"
    PB_D_COMP=0
    PB_SAMPLE_FN=_pb_s_dpkg
    _pb_watch "$PB_D_PID" || rc=$?
    PB_SAMPLE_FN=""
    if [ "$rc" -ne 0 ]; then
        [ -s "$logf" ] && cat "$logf" >&2
    fi
    return $rc
}

# ---------------------------------------------------------------------------
# 自建流程(tar | pigz, 再用 ar 拼 .deb)的进度:
# 此时 tar 不是直接子进程, 得在整棵进程树里找
# ---------------------------------------------------------------------------
PB_G_PID=0; PB_G_TOTAL=0; PB_G_OUT=""; PB_G_TMPD=""

_pb_s_pipe() {
    local k tp=0 r comp
    for k in $(_pb_descendants "$PB_G_PID"); do
        [ "$(cat "/proc/$k/comm" 2>/dev/null || true)" = tar ] && tp=$k
    done
    if [ "$tp" != 0 ]; then
        r=$(awk '/^rchar:/{print $2}' "/proc/$tp/io" 2>/dev/null || true)
        PB_S_LABEL="压缩"; PB_S_UNIT=bytes; PB_S_TOTAL=$PB_G_TOTAL
        PB_S_DONE=$(_pb_int "${r:-0}"); PB_S_NOTE=""
    else
        # tar 都退出去了 = 正在 ar 拼包, 分母用 control+data 的实际体积
        comp=$(_pb_int "$(stat -c%s "$PB_G_TMPD/data.tar.gz" 2>/dev/null || echo 0)")
        comp=$((comp + $(_pb_int "$(stat -c%s "$PB_G_TMPD/control.tar.gz" 2>/dev/null || echo 0)")))
        PB_S_LABEL="归档"; PB_S_UNIT=bytes; PB_S_TOTAL=$comp
        PB_S_DONE=$(_pb_int "$(stat -c%s "$PB_G_OUT" 2>/dev/null || echo 0)")
        PB_S_NOTE=""
    fi
}

# pb_track <total_bytes> <命令...>
pb_track() {
    local total="$1"; shift
    local rc=0
    if [ "$PB_MODE" = off ]; then
        "$@"
        return $?
    fi
    PB_G_TOTAL="$total"
    PB_G_OUT="$DEB_OUT"
    PB_G_TMPD="$TMP_DIR/ar"
    "$@" &
    PB_G_PID=$!
    PB_SAMPLE_FN=_pb_s_pipe
    _pb_watch "$PB_G_PID" || rc=$?
    PB_SAMPLE_FN=""
    return $rc
}
