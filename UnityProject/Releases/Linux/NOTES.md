# 开发笔记与决策记录

> 这份文件记录 debkit 是怎么来的、为什么这么设计、踩过哪些坑。
> `README.md` 讲**怎么用**，这份讲**为什么是这样**。
> 跟着工具目录一起走，换机器时一起拷过去。

最后更新：2026-10-26

---

## 1. 起因

需要把某个 Unity 打包的 Linux 桌面程序目录做成 `.deb`，要求：

- 装完能设开机自启动
- 桌面有快捷方式
- 图标用程序目录里的 `logo.jpg`
- **后续还有好几版程序要封**，所以要能复用，不是一次性脚本

---

## 2. 关键决策

### 2.1 装到 `/opt/<包名>/`，而不是拆到 `/usr`

程序是**一整坨**（自带 `UnityPlayer.so`、`GameAssembly.so`、
`_Data/` 全套资源），没法按 FHS 拆分。放 `/opt` 的好处：

- 卸载 = 删一个目录，不会漏文件
- 不和发行版文件混在一起，`dpkg -S` 不会被污染
- 正是 FHS 对 `/opt` 的定义：add-on application software
- Chrome / VMware / MATLAB 都这么干

### 2.2 不用 `fakeroot`，改用 `dpkg-deb --root-owner-group`

大部分教程会让你 `fakeroot dpkg-deb --build`。但 dpkg ≥ 1.19 自带
`--root-owner-group`，能把包内属主直接强制成 `root:root`，于是：

- 不用装 `fakeroot`
- 普通用户也能打出属主正确的包（不用 sudo）
- 少一层 shell 包装，出错信息更直白

**这一点直接决定了后来 chown 那个坑能不能干净地修掉**（见 3.3）。

### 2.3 压缩默认 gzip -6（2026-10-26 由 `xz -1` 改）

实测一个 Unity 大包源目录：

| 算法 | 体积 | 耗时 | 备注 |
|---|---|---|---|
| `gzip -6` | 约 8.3 GB | 约 1.5 分钟 | **现默认** |
| `xz -1` | 7.2 GB | 约 7 分钟 | 体积最优但慢 4~5 倍 |
| `zstd -6` | 约 8.2 GB | 约 40 秒 | 最快，但需 dpkg ≥ 1.21 |

**为什么改**：体积压不下去的根本原因是 `.bundle` / `.resS` **本身已经是压缩数据**，
再压几乎无收益 —— 从源到包的那点体积差里，绝大部分收益来自 `_Data/` 下的未压缩 `level*`
和 `sharedassets*.assets`。在这个前提下多花 5 分钟只换来 1 GB 左右，
对本地生成、U 盘拷贝的交付流程不划算。

小样实测（39 MB 可压缩内容，含大量重复 JSON 与零填充）：

| 算法 | 耗时 | 包体积 |
|---|---|---|
| `gzip -1` | 1.87 s | 2 232 604 |
| `gzip -6` | 2.02 s | 2 135 836 |
| `gzip -9` | 2.10 s | 2 120 714 |
| `xz -6` | 6.86 s | 639 340 |
| `zstd -6` | 1.87 s | 772 526 |

`-1 → -6` 只多 8% 时间却省 4.3% 体积，`-6 → -9` 多 4% 时间只省 0.7%，
所以默认取 `-6`。

**zstd 的可用性问题**（用户特别关心的点）：`-Zzstd` 是 dpkg 1.21 才加的，
还在用 1.19 / 1.20 的系统（不少老发行版和政企定制版都是）用不了。为此 `debkit.sh` 增加了 `comp_ok()`：

- 打包前先拿一个 1KB 空包真试一次 `dpkg-deb -Z<类型>`，比读文档可靠
- 配置写了 `zstd` 但本机不支持 → 自动降级 gzip，日志里给一行提示
- `COMPRESS="auto"` → 按 `zstd → gzip → xz` 顺序挑第一个可用的

这个检查必须在**复制 11 GB 之前**的压缩阶段做（用 `mktemp` 建空包），
否则老系统上等到压缩才失败，前面十几分钟白等。

### 2.4 `dpkg-deb -c` 的大包校验改为按需

`dpkg-deb -c` 会把整个包**解压一遍**：一个几 GB 的包要 **7 分多钟**。
早期版本在循环里反复调用它（还用来核对每个关键文件），等于解压七八遍。

现在：

- 包体积 > 2 GB 时自动跳过逐文件清单
- 只快速核对 `control.tar` 成员和几个关键路径
- 需要完整校验时加 `--verify`

### 2.5 自启动用 `~/.config/autostart` 覆盖，而不是直接改系统文件

包里放的是 `/etc/xdg/autostart/<包名>.desktop`（系统级，所有用户）。

开关工具在用户级 `~/.config/autostart/` 写一份同名文件，只写 `Hidden=true/false`。
按 XDG 规范**用户级优先**，好处：

- 开关不需要 root
- 升级重装不会重置用户的选择
- 每个用户可以有不同设置

---

## 3. 踩过的坑（按痛苦程度排序）

### 3.0 `EXTRA_ARGS` 曾经只对桌面图标生效（2026-10-26 修复）

早期版本把 `@EXTRA_ARGS@` 只渲染进 `app.desktop.in` 的 `Exec=` 行，
启动器 `/usr/bin/<包名>` 里根本没这个变量。后果：

| 入口 | 修前 | 修后 |
|---|---|---|
| 桌面图标 / 开始菜单 | 带参数 | 带 |
| **开机自启** | **不带** | 带 |
| **终端敲命令** | **不带** | 带 |

四个入口行为不一致，而且自启是无人值守的，最难发现。

改成**启动器统一注入**：`app.desktop.in` 的 `Exec=` 只写 `/usr/bin/<包名>`，
参数全部由启动器的 `EXTRA_ARGS` 负责。理由：四个入口都走同一个启动器，
只有一处需要维护，也不可能出现「两个入口各加一份、参数重复」的情况。

几个实现取舍：

- 语义是「用户没传参才用默认值」（`[ "$#" -eq 0 ]`），不是叠加。
  这样 `./包名 -screen-fullscreen 0` 能干净覆盖，而不是变成两个矛盾的参数。
- 用 `eval "set -- $EXTRA_ARGS"` 而不是 `set -- $EXTRA_ARGS`。
  后者不做二次展开，`$HOME` 会原样传给程序；前者既支持 `$HOME` 按当前用户
  展开，也支持 `-name "a b"` 这种含空格的单个参数。安全性上不亏：配置文件
  由打包者控制，与包内其他脚本（postinst 是 root 执行）同等可信。
- 写进模板前做 `sed 's/[\\"$`]/\\&/g'` 转义（`debkit.sh` 的 `EXTRA_ARGS_SH`），
  否则参数里一个 `"` 就能截断字符串、把启动器语法搞坏。
- 日志里记 `args=...`，出问题时 `grep args= launch.log` 就知道实际传了什么。

实测（小样例，`EXTRA_ARGS="-logFile \$HOME/out.log -screen-fullscreen 1 -name \"a b c\""`）：

```
无参数       -> [-logFile] [/tmp/t1/out.log] [-screen-fullscreen] [1] [-name] [a b c]
用户传 -windowed -> [-windowed]                      ← 覆盖，不是叠加
--autostart  -> 同第一行，且 --autostart 没被透传给程序
```

### 3.1 本机 `/tmp` 只有 10 MB

这是个 tmpfs。`dpkg-deb` 默认把 tar 中间产物放 `/tmp`，11 GB 的包直接
`设备上没有空间`。

解决：脚本里 `export TMPDIR=<work 目录>`，并让用户可用 `-w` 指定到空间充足的分区。

### 3.2 `chown` 报一堆「不允许的操作」并中断构建

现象：

```
chown: 正在更改'<工作目录>/work/.../StreamingAssets' 的所有者: 不允许的操作
...（几百行）
```

根因有两层：

1. `chown -R 0:0` 在**普通用户**下必然被内核拒绝（改属主只有 root 能做）
2. 脚本开头是 `set -Eeuo pipefail`，`chown` 遍历完返回 1 → **脚本整体退出**

于是看起来像"打了一半崩了"。

但这一步本来就多余：`--root-owner-group` 会覆盖包内属主。修法：

```sh
# 复制阶段不再尝试保留属主
cp -a --no-preserve=ownership "$SRC_DIR"/. "$DEST_DIR"/

# 归一化阶段：只有 root 才 chown，且失败不致命
if [ "$(id -u)" = 0 ]; then
    chown -R 0:0 "$ROOT" 2>/dev/null || warn "部分文件属主未能修改 (不影响安装…)"
else
    printf '  以普通用户运行: 跳过 chown, 包内属主由 --root-owner-group 统一\n'
fi
```

顺带发现：这台机器上 **root 也无法 `chown` 到非 0 属主**（报
`无效的参数` EINVAL），所以已安装文件的属主没法改回去，只能删掉重建。

### 3.3 交互式向导 `read` 在子 shell 里，EOF 后死循环

最初写成 `x=$(ask "问题")`，`read` 跑在命令替换的子 shell 里，
脚本收到 EOF 后 `exit` 传不到父进程，`while :` 就一直空转。

改成把结果写全局变量 `ASK_REPLY`，输入流结束直接 `die`，`y/n` 最多问 3 次。

### 3.4 `wizard.sh` 里一个多余的双引号

```sh
for f in "$d"/*.x86_64 "$d"/*.x86";   # 末尾多了一个 "
```

导致整个文件进入未闭合字符串状态，`bash -n` 报错位置离真正原因很远。
教训：**报错行号不可信时，用「含奇数个双引号的行」定位**
（`awk '{n=gsub(/"/,"\""); if(n%2==1) print NR}'`）。

### 3.5 桌面 `.desktop` 必须 `755`

`/usr/share/applications/` 里的 644 就够（菜单系统读），
但 `~/桌面/` 里的必须可执行，否则 GNOME 会提示"不受信任的启动器"，双击无反应。

### 3.6 `logo.jpg` 是白底方形图

直接缩放的话，深色主题下桌面上是个白方块，很难看。

做法：ImageMagick 从**四个角 floodfill 成透明**（只对与角落连通的白色生效，
不会误伤图形内部的白色），再生成 16～512 共 8 个尺寸。失败时退化为直接缩放并告警。

### 3.7 普通用户写不进共享目录

工具目录是从共享路径 `root:root 775` 那种方式拷来的。普通用户跑工具时，
`work/` 和 `output/` 都写不进去。

现在启动时检测，不可写就自动退到 `~/.local/share/debkit/{work,output}` 并打印提示。

---

## 4. 本次向框架合并的改动（2026-10-29）

这一轮是**从实际业务项目向通用框架做合并**，改了 8 处。逐条记下"为什么这么定"，
免得以后又绕回原路。

### 4.1 删掉 `pack-self.sh` / `apps/` 改 `appConfigs/` / 删 `build.log`

| 删/改 | 原因 |
|---|---|
| `pack-self.sh` | 它把 debkit 自己打成 deb + `.gz.run`，只服务于"把这套工具装到目标机"这一个用途。实际项目里工具就在 WSL 侧目录里，直接 `./debkit.sh` 更简单；维护两套安装路径（`--zip` 的自解压逻辑有 60 行）不值得 |
| `apps/` → `appConfigs/` | `/usr/share/applications`、`~/.config/autostart` 里到处是 "apps"，`ls apps/` 容易和目标机的应用目录混；`appConfigs/` 语义更准 |
| `work/build.log` | 里面是当时某次构建的绝对路径和用户目录，属于运行产物，不该进仓库。`work/.gitkeep` 保留 |

### 4.2 `-f/--force` 与 `CLI_OUT` 是死代码

`FORCE` 变量解析出来之后**从头到尾没有任何一处读它**；`CLI_OUT` 同理（`-o/--outdir` 走的是
另一条赋值路径）。死参数比没参数更糟：用户以为加了 `-f` 会强制覆盖，实际什么都不发生。

一起清掉的还有 `lib/progress.sh` 的保留决定 —— 这个**保留**，压缩阶段进度条是有用的。

### 4.3 压缩：加 `zstd` 与 `pigz`，默认仍是 `gzip -6`

- `gzip -6` 保持默认：所有 Debian 系 dpkg 都认，不挑环境。
- `pigz` 输出的是**标准 gzip 流**，只换压缩器不换格式，`dpkg` 完全无法区分 ——
  所以 `auto` 把它排在第一优先，比 zstd 更安全（不受 dpkg 版本限制）。
- `zstd` 需要 dpkg ≥ 1.21，旧版 dpkg 用不了；写 `zstd` 而本机不支持时**自动降级**到 gzip，
  不盲试。
- 实测大包：gzip 1.5 分钟 / pigz 约 20 秒 / xz 约 7 分钟 / zstd 约 40 秒，体积差异 < 15%。

### 4.4 `QUALITY_DEFAULT` 只收数字索引

**踩过的坑**：最早的实现让 `-ctl quality` 接受 `low/middle/high`，再映射成
`Performant/Balanced/HighFidelity` 三个档位名。问题有三个：

1. **档位名每个项目都不一样**。本仓库 `ProjectSettings/QualitySettings.asset` 实际是
   `Very Low / Low / Medium / High / Very High / Ultra` 六档，和上面三个名字毫无关系。
2. Unity 的 `QualitySettings.SetQualityLevel` 参数是 **int 索引**，名字映射是多余的一层。
3. 换项目就要改脚本，等于没做成通用实现。

改成只收数字（0 起，顺序同 Project Settings > Quality 列表）：
脚本不关心档位叫什么，主程序用 `SetQualityLevel(int)` 应用，换项目零改动。
越界/非数字由主程序打 warning 并保持当前档位，不会拿错的档位启动。

代价是用户得知道自己的档位顺序 —— 用 `<包名>-ctl quality status` 能看到当前索引，
README 里也写清了。

### 4.5 `vulkan` → `graphics`，默认 `none`

原来的开关只能"管 Vulkan"（`-force-vulkan` / 不传），语义偏一半：想强制 OpenGL 没法表达。
改成三值：

| 值 | 传参 |
|---|---|
| `opengl` | `-force-glcore` |
| `vulkan` | `-force-vulkan` |
| `none`（默认） | 不传，由 Unity 按 Auto Graphics API 自己挑 |

`/etc/<包名>/vulkan` → `/etc/<包名>/graphics`。`postrm` 里顺带清掉旧文件名，
因为留着一个 `vulkan` 会让重装后的机器在没有 Vulkan 驱动时直接起不来。

**默认 `none`** 而不是捆绑某个后端：目标机显卡驱动千差万别，替用户决定一个后端
就是把"起不来"的责任揽到自己身上。

命令行里出现任意 `-force-vulkan` / `-force-glcore` / `-force-gles*` / `-force-d3d*` 时
以命令行为准，不再追加 —— 否则会出现两个互相矛盾的接口参数让 Unity 行为未定义。

### 4.6 `CREATE_DESKTOP_ICON` 保留下来并真正实现

之前配了这个字段但没人读。现在 postinst 里读：

- `yes`（默认）：装包时遍历本地用户建好桌面快捷方式（原行为）
- `no`：跳过，只打一行说明；用户之后可以自己 `<包名>-ctl icon create`

和 `-ctl icon create/remove` 是**两件事**：前者管装包那一刻，后者管装完之后随时开关。
两个都要，因为"内网批量部署不想让用户桌面被塞东西"和"用户自己想加一个"是不同诉求。

### 4.7 `--no-build` / `-Z none` 不再强制要求 `dpkg-deb`

原来的依赖检查在解析参数之后无条件要求 `dpkg-deb`，导致**调试模式的调试模式都跑不了**：
`--no-build` 明明只需要生成安装树，却因为没装 dpkg 直接退出。
现在只有真正要产出 `.deb` 时才检查 `dpkg-deb`。

顺带的好处：在纯 Windows Git Bash（没有 dpkg）里也能 `--no-build -k` 把安装树生成出来
对照模板改脚本。

### 4.8 `.gitattributes` 补 `*.sh` / `*.in` 强制 LF

`* text=auto` 只保证"入库时归一化"，**检出时仍按平台默认换行**。在 Windows 上克隆
就是 CRLF，拷进 WSL 直接 `bad interpreter: /usr/bin/env bash^M`。
显式 `eol=lf` 才能保证检出即正确。

---

## 5. 已验证的事实

### 5.1 打包产物（2026-10-26 实测，业务项目）

| 项目 | 结论 |
|---|---|
| 源 → 包 | 压缩后约为源体积的 60%~70% |
| 构建耗时 | 约 3 分钟（复制 + 校验和 + gzip -6 压缩） |
| 包内属主 | `root/root`（`dpkg-deb -c` 逐条确认） |
| 包内权限 | 目录 755，文件 644，可执行/动态库 755 |
| `md5sums` | `md5sum -c` 全通过 |
| 集成文件 | `/usr/bin/<包名>`、两个 `.desktop`、图标、`user-setup.sh`、主程序均在包内 |
| 自启动开关 | `on/off/toggle/status` 四个动作实测，写入 `~/.config/autostart` 后 `Hidden` 字段正确 |
| 非 root 构建 | 模拟 uid=1000 完整跑通，零 chown 报错，属主仍是 root/root |
| 运行身份 | 桌面/菜单/自启全部以登录用户身份运行，无 sudo/pkexec/setuid |

**没验证的**：当时的环境不允许直接 `dpkg -i`，所以真实安装流程是用
`dpkg-deb -x/-e` 解包 + 校验 + 实跑脚本模拟的。真机安装请自己再确认一次，
重点看桌面图标和开机自启。

### 5.2 本次合并后的回归（2026-10-29）

`dpkg-deb` 不可用（纯 Windows Git Bash）环境下能做的部分全部做了：

| 检查 | 结果 |
|---|---|
| `bash -n` 全部脚本与模板 | 通过（模板先替换 `@VAR@` 占位符再检查） |
| `--no-build -k` 全链路 | 通过，生成 `/opt` + `/usr/bin` + `/usr/lib` + `.desktop` + `DEBIAN/*` 共 11 个文件 |
| `postinst` 变量注入 | `QUALITY_DEFAULT="4"`、`GRAPHICS_DEFAULT="opengl"`、`CREATE_DESKTOP_ICON="yes"` 均正确替换 |
| 启动器拼参（8 种组合） | 见下表，全部符合预期 |

启动器拼参实测（两段逻辑从生成的 `/usr/bin/<包名>` 里原样抽出，在假 `/etc` 下跑）：

| 输入 | 实际传给程序 | 判定 |
|---|---|---|
| 两文件都不存在，默认 4/opengl | `-picture-quality 4 -force-glcore` | ✅ 兜底生效 |
| quality=3 | `-picture-quality 3 -force-glcore` | ✅ |
| quality=3 graphics=vulkan | `-picture-quality 3 -force-vulkan` | ✅ |
| quality=3 graphics=none | `-picture-quality 3` | ✅ 不传接口参数 |
| 命令行 `-picture-quality 1` | `-picture-quality 1 -force-glcore` | ✅ 命令行优先 |
| 命令行 `-force-vulkan` | `-force-vulkan -picture-quality 5` | ✅ 不重复加接口参数 |
| 命令行 `-force-glcore` | `-force-glcore -picture-quality 5` | ✅ |
| quality=`abc` graphics=`GL` | `-force-glcore` | ✅ 非数字忽略，大小写归一 |

**没验证的**：真实 `dpkg -i` 安装、`gtk-update-icon-cache`、桌面图标实际落盘 ——
需要 Debian 系机器，WSL 里跑一次 `sudo dpkg -i` 即可补齐。

---

## 6. 已知限制

- **只支持 Debian 系**（`dpkg`）。要出 rpm 得另写一套，`spec` 文件结构完全不同。
- **`ARCH` 写死 amd64**。要出 arm64 包得改配置并保证程序本体是 arm64 编译的。
- **图标依赖 ImageMagick**。没有的话跳过图标，桌面显示默认齿轮，不报错也不阻塞。
- **升级不保留 `/opt` 下用户自建文件**。`dpkg` 升级会覆盖整个目录，
  如果用户在安装目录里放了东西会被清掉（需要保留的用 `WRITABLE_DIRS`）。
- **画质索引与档位名的对应关系在程序侧**。工具只传数字，不校验范围（不同项目档位数不同）。
  写错范围时主程序打 warning 并保持当前档位，不会崩，但用户会以为"切了没反应"。

---

## 7. 目录里每个文件的作用

```
debkit.sh              唯一入口：解析参数 → 校验 → 复制 → 生成集成文件 → 打包 → 自检
lib/common.sh          日志函数、模板渲染、命令检查
lib/icons.sh           logo → 多尺寸图标（含四角去白底）
lib/wizard.sh          交互式向导（默认值靠自动探测，含 Unity Quality/接口问题）
lib/progress.sh        压缩阶段进度条：读 /proc/<pid>/rchar 估算进度
templates/
  launcher.sh.in       /usr/bin/<包名>：cd 安装目录 + 单实例锁 + 日志 + root 提示
                       + 拼 -picture-quality / -force-*
  appctl.sh.in         /usr/bin/<包名>-ctl：autostart / quality / graphics / icon / run
  user-setup.sh.in     postinst/prerm 调用：遍历本地用户建/删桌面快捷方式
  app.desktop.in       开始菜单项
  autostart.desktop.in 系统级自启动项
  autostart-toggle.desktop.in  菜单里的一键切换入口
  postinst.in          刷新图标缓存/菜单库、建桌面快捷方式、写 quality/graphics 默认值
  prerm.in             停止运行中的程序
  postrm.in            清理各用户的快捷方式、自启动项、日志、/etc 下的开关文件
appConfigs/example.conf  配置示例（复制改名成自己的）
output/                产出的 .deb
work/                  打包暂存
```

### 7.1 为什么 `debkit.sh` 不能直接放 `/usr/bin`

`debkit.sh` 靠 `BASH_SOURCE[0]` 的所在目录定位 `lib/` 和 `templates/`。
放进 `/usr/bin` 后 `DEBKIT_DIR` 会变成 `/usr/bin`，`source lib/common.sh` 立刻失败。
所以如果需要系统级安装，`/usr/bin/debkit` 只能是一层转发脚本：

```sh
#!/bin/sh
exec /usr/share/debkit/debkit.sh "$@"
```

`exec` 后 `BASH_SOURCE[0]` 就指向真实位置了。

由此带来的两个"只读"问题，靠回退解决：

| 变量 | 可写时 | 不可写时 |
|---|---|---|
| `APPS_DIR` | `<脚本目录>/appConfigs` | `~/.config/debkit/appConfigs` |
| `OUTDIR` / `WORKROOT` | `<脚本目录>/{output,work}` | `~/.local/share/debkit/{output,work}` |

`SYS_APPS_DIR` 始终保留，**查找配置时两个目录都看**（同名用户目录优先）。

`-c` 支持只写名字：`resolve_conf()` 依次试
`APPS_DIR/<名>`、`APPS_DIR/<名>.conf`、`SYS_APPS_DIR/<名>`、`SYS_APPS_DIR/<名>.conf`。
注意 `set -e` 下要用 `CONFIG="$(resolve_conf "$2" || true)"` 包一层，
否则找不到时函数返回 1 会让脚本直接退出。

### 7.2 下一版程序的打包流程

```bash
cd <项目>/Releases/Linux
cp appConfigs/example.conf appConfigs/myapp-v2.conf
# 改 4 个字段：APP_TITLE / PKG_NAME / VERSION / SRC_DIR（+ EXECUTABLE）
./debkit.sh -c appConfigs/myapp-v2.conf

# 或者不写配置，直接问答：
./debkit.sh
```

想覆盖升级旧版就**别改 `PKG_NAME`**，只提高 `VERSION`（或 `-r` 加修订号）。

WSL 下记得 `-w ~/debkit-work` 让暂存目录离开 `/mnt`。
