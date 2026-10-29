#!/usr/bin/env bash
# debkit - 图标生成
# 把 make_icons <源图> <输出目录> <包名> [尺寸列表]
# 输出: <输出目录>/icons/<尺寸>x<尺寸>/apps/<包名>.png
#       <输出目录>/pixmap/<包名>.png
#
# 说明: JDK 常见做法是源图是带白底的方形 logo。为了在深色桌面主题下也好看,
#       会把四角的白底 floodfill 成透明(不会误伤图形内部的白色区域)。

make_icons() {
  local src="$1" outroot="$2" pkg="$3"
  local sizes="${4:-16 24 32 48 64 128 256 512}"
  local master="$outroot/.icon-master.png"

  if ! command -v convert >/dev/null 2>&1; then
    warn "未找到 ImageMagick 的 convert, 跳过图标生成(桌面将显示默认图标)"
    return 1
  fi
  [ -f "$src" ] || { warn "logo 文件不存在: $src"; return 1; }

  mkdir -p "$outroot/icons" "$outroot/pixmap"

  # 1) 统一成 1024x1024 再做白色角落透明化
  if ! convert "$src[0]" -resize 1024x1024! -alpha set \
        -bordercolor white -border 2 -fuzz 10% \
        -fill none \
        -draw "color 0,0 floodfill" \
        -draw "color 1027,0 floodfill" \
        -draw "color 0,1027 floodfill" \
        -draw "color 1027,1027 floodfill" \
        -shave 2x2 -strip PNG32:"$master" 2>/dev/null; then
    warn "透明化处理失败, 退化为直接缩放(白底保留)"
    convert "$src[0]" -resize 1024x1024! -strip PNG32:"$master" \
      || { warn "图标转换失败: $src"; return 1; }
  fi

  # 2) 生成各尺寸
  local s
  for s in $sizes; do
    mkdir -p "$outroot/icons/${s}x${s}/apps"
    convert "$master" -resize "${s}x${s}" -strip \
      "$outroot/icons/${s}x${s}/apps/${pkg}.png" 2>/dev/null \
      || warn "生成 ${s}x${s} 图标失败"
  done

  # 3) pixmaps 兜底(部分老桌面只认这个路径)
  cp -f "$master" "$outroot/pixmap/${pkg}.png"
  convert "$outroot/pixmap/${pkg}.png" -resize 256x256 -strip \
    "$outroot/pixmap/${pkg}.png" 2>/dev/null || true

  rm -f "$master"
  ok "图标已生成 ($(echo $sizes | wc -w) 个尺寸)"
}
