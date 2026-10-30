# debkit — 一键把程序目录打成 .deb

面向 Unity / Qt / 任意「可执行文件 + 资源目录」的 Linux 桌面程序的打包工具。
一条命令产出可安装的 `.deb`，装完自动带 **开始菜单项**、**桌面快捷方式**、**可开关的开机自启动**、
**画质档位切换** 和 **图形接口切换**。

> 使用手册就是这份 `README.md`；**设计取舍、踩坑记录、已验证结论**在同目录的
> [`NOTES.md`](NOTES.md)。改这个工具之前建议先翻一遍，能省不少时间。

---

## 0. 环境要求

几乎不用装东西：**任何 Debian 系发行版自带这些**。
换机器后可以先跑一次自检：

```bash
./debkit.sh --check
```

```
==> 打包环境自检
 [ OK ] bash             5.0.17(1)-release
 [ OK ] dpkg-deb         1.21.6
 [ OK ] owner            支持 --root-owner-group, 包内属主可强制 root:root(无需 sudo)
 [ OK ] coreutils        cp find du md5sum sed awk chmod mkdir 齐全
 [ OK ] compress         可用: gzip xz zstd (默认 gzip -6)
 [ OK ] imagemagick      6.9.10-23 (可生成多尺寸/透明图标)
 [ OK ] disk             暂存目录所在分区可用 42G (建议 >= 源体积的 2 倍)
```

| 组件 | 必需 | 用途 | 装法 |
|---|---|---|---|
| `bash` ≥ 4 | 是 | 跑脚本本身 | 系统自带 |
| `dpkg-deb` ≥ 1.19 | 是 | 生成 `.deb`。≥1.19 才有 `--root-owner-group`，没有它就得 `sudo` 打包 | `sudo apt install dpkg` |
| `coreutils`（`cp/find/du/md5sum/chmod`） | 是 | 复制、算校验和、归一化权限 | `sudo apt install coreutils` |
| `sed` `awk` | 是 | 配置解析、模板渲染 | 系统自带 |
| **pigz** | 否 | 多线程 gzip。大包压缩快一个数量级，产物仍是标准 gzip 流，兼容性同 gzip | `sudo apt install pigz` |
| **ImageMagick**（`convert`） | 否 | `logo.jpg` → 8 种尺寸 hicolor 图标 + 去白底透明化 | `sudo apt install imagemagick` |
| `zstd` | 否 | 仅当配置 `COMPRESS="zstd"` 时用，压得最快、包略大。**dpkg ≥ 1.21 才有** | — |

说明几点：

- **不需要 `fakeroot`**。很多教程会让你用 `fakeroot dpkg-deb --build`，这里换成 `--root-owner-group`，免去装 `fakeroot`，也免去必须用 root。
- **不需要 `dh_make` / `debuild` / `dpkg-dev`**。那套是打"源码包→发行版包"的流程，这里是二进制包直接封装，用不到。
- **没装 ImageMagick 也能打包**，只是图标那一步跳过，桌面上会显示系统默认的齿轮图标。
- **ImageMagick 7 只有 `magick` 命令**（没有 `convert`）时，自检会标 `!!` 并说明；此时装一个 ImageMagick 6 的兼容包，或手工把 `lib/icons.sh` 里的 `convert` 换成 `magick`。
- **没装 pigz 也能打包**，只是压缩回到单线程 `gzip`。`COMPRESS="auto"` 会按 `pigz → zstd → gzip → xz` 挑第一个可用的。
- 磁盘：暂存目录需要 **源体积 + deb 体积** 的空间，`--check` 会把可用空间打出来。

> **注意自检本身也需要 `dpkg-deb`**。只有 `--no-build` / `-Z none`（只生成安装树、不产 deb）时才允许缺 `dpkg-deb`，
> 这样在没装 dpkg 的地方（例如纯 Windows 的 Git Bash）也能把安装树生成出来看结构。

---

## 1. 快速开始

```bash
cd Releases/Linux          # 本工具目录(Unity 工程内的 Releases/Linux)

./debkit.sh                                      # 交互式向导(推荐首次使用)
./debkit.sh -l                                   # 看有哪些应用配置
./debkit.sh -c appConfigs/example.conf           # 直接按配置打包
```

不带任何参数、且在终端里运行时，会自动进入交互式向导。

### 交互式向导长什么样

```
==> 交互式打包向导
  已有配置:
   1) example                              My Application
   0) 新建一份配置
  选择 [0]:
  程序所在目录 [.../Releases/Linux/build]:
  自动识别到主程序: MyApplication.x86_64
  主可执行文件 [MyApplication.x86_64]:
  logo 图片(相对程序目录) [logo.jpg]:
  程序显示名(可中文) [MyApplication]:
  包名(小写, 也是启动命令) [my-application]:
  版本号 [1.0.0]:
  ...
  默认画质索引(数字 0 起, 同 Unity Quality 列表; none=由程序决定) [none]:
  默认图形接口 opengl(强制 OpenGL)/vulkan(强制 Vulkan)/none(由 Unity 自己挑) [none]:
  安装后默认开机自启 (y/n) [yes]:
```

- 每项都能直接回车走默认值，默认值本身是从目录里**自动探测**出来的
  （主程序、logo、显示名、包名、安装路径、源目录）。
- 选一个已有配置 + 回车，就是「按上次的设置再打一次」。
- 问完后会把答案**存成一份 `appConfigs/<包名>.conf`**，下次可以直接 `-c` 调用，也可以手工再改字段。

产物：`output/<包名>_<版本>_<架构>.deb`

```bash
sudo dpkg -i output/my-application_1.0.0-1_amd64.deb
sudo apt-get -f install        # 若提示缺依赖
```

常用参数：

| 参数 | 说明 |
|------|------|
| `-i, --interactive` | 进入交互式向导 |
| `-c, --config` | 指定应用配置（默认 `appConfigs/example.conf`） |
| `-v, --version` | 覆盖版本号 |
| `-r, --revision` | 覆盖打包修订号（同版本重打包时 +1） |
| `-s, --source` | 覆盖 `SRC_DIR`（临时换目录出包用） |
| `-o, --outdir` | 输出目录 |
| `-w, --workdir` | 暂存目录（大工程放空间充足的分区） |
| `-Z, --compress` | 压缩算法：`pigz` `gzip` `xz` `zstd` `none` `auto`（默认取配置） |
| `-z, --level` | 压缩级别 1–9（默认取配置，一般 6） |
| `--autostart yes\|no` | 临时覆盖「装完默认开机自启」 |
| `-k, --keep-stage` | 保留安装树，便于检查 |
| `--verify` | 强制逐文件校验产物（大包会多花几分钟） |
| `--no-build` | 只生成安装树不打包（调试，不需要 `dpkg-deb`） |
| `--progress` / `--no-progress` | 强制开/关进度条（默认终端里自动开，重定向时每 5 秒一行） |
| `--clean` | 清空 `work/` 和 `output/` |
| `-l, --list` | 列出 `appConfigs/` 下所有配置 |
| `--check` | 检查本机打包环境（依赖、压缩后端、磁盘），换机器先跑这个 |

---

## 2. 目录结构

```
Releases/Linux/
├── debkit.sh                     # 主程序（唯一入口）
├── appConfigs/
│   └── example.conf              # 配置示例（复制改名成自己的）
├── lib/
│   ├── common.sh                 # 日志 / 模板渲染 / 校验
│   ├── icons.sh                  # logo → 多尺寸图标（自动去白底）
│   ├── wizard.sh                 # 交互式提问，生成 appConfigs/*.conf
│   └── progress.sh               # 压缩阶段进度条（读 /proc/<pid>/rchar）
├── templates/
│   ├── launcher.sh.in            # /usr/bin/<包名> 启动器
│   ├── appctl.sh.in              # /usr/bin/<包名>-ctl 控制工具
│   ├── user-setup.sh.in          # 给所有用户建/删桌面快捷方式
│   ├── app.desktop.in            # 开始菜单项
│   ├── autostart.desktop.in      # 系统级自启动项
│   ├── autostart-toggle.desktop.in
│   └── postinst.in / prerm.in / postrm.in
├── build/                        # Unity 出的 Linux player 产物（SRC_DIR 默认指这里，需先构建一次）
├── output/                       # 生成的 deb（可随时删）
└── work/                         # 打包暂存（可随时删）
```

`build/` 由 Unity 构建流程产出（`BuildConfig` 里 Linux 输出就是 `Releases/Linux/build/`），首次打包前先出一次 Linux player；
`output/` 和 `work/` 是 debkit 的运行产物，删掉不影响使用。

**配置查找顺序**：`-c` 给的路径 → `~/.config/debkit/appConfigs/` → 本目录 `appConfigs/`。
所以 `-c example` 只写名字即可，不用写目录和后缀。

> 目录名用 `appConfigs/` 而不是 `apps/`，是为了和系统里 `/usr/share/applications` 之类的
> "apps" 区分开，避免在 `ls`/补全时看花眼。

### 在 Windows 上用 WSL 构建

Unity 出的是 Linux player（`BuildTarget.StandaloneLinux64`，输出到 `Releases/Linux/build/<产品名>`），
而构建机是 Windows。**不要让 WSL 停在 `/mnt/e` 上打包**，否则会踩三个坑：

| 坑 | 现象 | 解决 |
|---|---|---|
| `/mnt/<盘>` 的 `chmod` 是空操作 | 包内可执行文件权限变成 `0777`，`dpkg` 报警 | `-w ~/debkit-work`，让暂存目录落在 ext4 上 |
| CRLF 行尾 | `#!/usr/bin/env bash\r`，`bad interpreter` | 仓库 `.gitattributes` 已对 `*.sh`/`*.in` 强制 `eol=lf`；自己拷文件注意别被 Windows 编辑器改行尾 |
| 可执行位丢失 | 源目录里的 `UnityPlayer.so` / 主程序没有 `+x` | 打包前 `ls -l build/` 确认；工具会自动给主程序补 `+x`，但 `.so` 不会 |

推荐的 WSL 调用方式：

```bash
# 1) 先确认主程序实际叫什么（Unity 目录名是产品名，二进制名未必带 .x86_64）
ls -l /mnt/e/Unity/TEngine/UnityProject/Releases/Linux/build/

# 2) 暂存目录放到 ext4，源目录保持在 /mnt 上读
cd /mnt/e/Unity/TEngine/UnityProject/Releases/Linux
./debkit.sh -c appConfigs/example.conf -w ~/debkit-work

# 3) 需要先看安装树结构（不产 deb，不需要 dpkg-deb）
./debkit.sh -c appConfigs/example.conf --no-build -k
```

WSL 里装 `pigz` 和 `imagemagick` 就能拿到完整速度和多尺寸图标：
`sudo apt install pigz imagemagick`。

`/tmp` 容量小之类的问题属于**目标机**限制，在 WSL 构建时不适用；
相关记录见 [`NOTES.md`](NOTES.md)。

---

## 3. 装完之后系统里有什么

| 路径 | 作用 |
|------|------|
| `/opt/<包名>/` | 程序本体（原目录整体搬入） |
| `/usr/bin/<包名>` | 启动器（自动 cd 到安装目录、单实例保护、日志、注入画质/图形接口参数） |
| `/usr/bin/<包名>-ctl` | 自启动 / 画质 / 图形接口 / 桌面快捷方式开关 |
| `/usr/lib/<包名>/user-setup.sh` | 给所有本地用户批量建/删桌面快捷方式（供 postinst 与 `-ctl icon` 调用） |
| `/etc/<包名>/quality` | 当前画质档位索引（存在才生效） |
| `/etc/<包名>/graphics` | 当前图形接口设置 `opengl`/`vulkan`/`none`（存在才生效） |
| `/usr/share/applications/<包名>.desktop` | 开始菜单项 |
| `/usr/share/applications/<包名>-autostart.desktop` | 菜单里的「开机自启动（点此切换）」 |
| `/etc/xdg/autostart/<包名>.desktop` | 系统级自启动项 |
| `/usr/share/icons/hicolor/*/apps/<包名>.png` | 8 种尺寸图标（16～512） |
| `~/桌面/<包名>.desktop` | 桌面快捷方式（安装时给每个本地用户建好） |

### 控制命令

```bash
<包名>-ctl autostart on|off|toggle|status     # 开机自启动(只影响当前用户)
<包名>-ctl quality <索引>|status|reset        # 画质档位
<包名>-ctl graphics opengl|vulkan|none|status|reset   # 图形接口
<包名>-ctl icon create|remove                 # 桌面快捷方式
<包名>-ctl run [参数...]                     # 直接按启动器逻辑运行(排障用)
```

`quality` / `graphics` 写的是 `/etc/<包名>/` 下的整机文件，所以需要提权，工具会自动提示。

### 画质档位怎么用

只接受**数字索引**，顺序和 Unity **Project Settings > Quality** 列表一致（本项目是
`Very Low / Low / Medium / High / Very High / Ultra`，对应 0–5）：

```bash
<包名>-ctl quality 4          # 下次启动切到第 5 档(Very High)
<包名>-ctl quality status
<包名>-ctl quality reset
```

为什么用索引而不是名字：**档位名每个项目都不一样**，写死名字就要为每个项目改脚本。
索引由主程序侧解析（本仓库 `ProcedureLaunch.ApplyQualityFromCommandLine` 用
`QualitySettings.SetQualityLevel(int)` 应用），换项目、改档位名都不用改这里。
越界或非数字时主程序会打一行 warning 并保持当前档位，不会拿错误的档位启动。

启动器把 `/etc/<包名>/quality` 读出来，追加成 `-picture-quality <索引>`。
**命令行里已经带了 `-picture-quality` 时以命令行为准**（不叠加）。

### 图形接口怎么切

```bash
<包名>-ctl graphics opengl    # → -force-glcore
<包名>-ctl graphics vulkan    # → -force-vulkan
<包名>-ctl graphics none      # → 不传参数, 由 Unity 按 Auto Graphics API 自己挑(默认)
<包名>-ctl graphics status
```

命令行里出现了任意 `-force-vulkan` / `-force-glcore` / `-force-gles*` / `-force-d3d*` 时同样以命令行为准，
不会出现两个互相矛盾的接口参数。**没有 Vulkan 驱动的机器上设成 `vulkan` 会起不来**，此时改回 `none` 或 `opengl`。

### 开机自启动怎么控制

三种方式，任选：

```bash
# 1) 命令行
<包名>-ctl autostart on      # 开启
<包名>-ctl autostart off     # 关闭
<包名>-ctl autostart status  # 查看

# 2) 开始菜单里点「<显示名> 开机自启动(点此切换)」

# 3) 打包时就定下来（appConfigs/*.conf 里）
AUTOSTART_DEFAULT="yes"     # 或 no
AUTOSTART_DELAY="3"         # 登录后等 3 秒再启动，等桌面就绪
```

原理：包里放的是 `/etc/xdg/autostart/<包名>.desktop`，开关工具在
`~/.config/autostart/<包名>.desktop` 写一份用户级覆盖（`Hidden=true/false`），
按 XDG 规范用户级优先，所以开关对所有桌面环境都生效，且不需要 root。

启动器带 `--autostart` 时会：等 X 会话出现 → 延迟 N 秒 → 单实例锁 → 启动，
并把输出写进 `~/.local/state/<包名>/launch.log`。

### 装包时要不要自动建桌面快捷方式

配置里 `CREATE_DESKTOP_ICON`：

| 值 | 装包时行为 |
|---|---|
| `yes`（默认） | postinst 遍历所有本地用户，直接建好 `~/桌面/<包名>.desktop` |
| `no` | 跳过这一步，只在安装输出里说明；用户之后可以自己跑 `<包名>-ctl icon create` |

注意这两件事是分开的：`CREATE_DESKTOP_ICON` 管**装包那一刻**，
`<包名>-ctl icon create/remove` 管**装完之后随时开关**。关掉前者不影响后者。

### 以什么身份运行 / 权限边界

**程序以「当前登录的普通用户」身份运行，不会提权。**

| 环节 | 身份 |
|---|---|
| `sudo dpkg -i` 安装 | root（只有这一步） |
| 桌面双击图标 | 登录用户 |
| 开始菜单启动 | 登录用户 |
| 开机自启动 | 登录用户（不经过 root） |

包里没有任何 `sudo` / `pkexec` / setuid 位，启动器就是 `exec /opt/<包名>/<可执行文件>`，
继承调用者的 uid。

安装目录的权限边界：

```
/opt/<包名>/          755  root:root    ← 普通用户可读、可执行，不可修改
/opt/<包名>/*.so      755  root:root
/opt/<包名>/数据文件   644  root:root
```

普通用户**不能**改动安装目录，只有 root（`sudo`）能。这是有意为之：
程序文件属于管理员，用户改不动，就不会因为误删资源文件而"装好了又坏"。

**程序需要往安装目录里写**的时候（配置文件 / StreamingAssets / 录像），用 `WRITABLE_DIRS`
把这几个子目录的属主交给运行用户，而不是整体 root 运行：

```ini
# 相对 INSTALL_DIR，空格分隔，可写多个
WRITABLE_DIRS="configs StreamingAssets/logs"
```

postinst 每次 configure 都会执行一次，所以升级把文件换回 `root:root` 后会被自动纠正。
属主取自 `SUDO_USER`，没有就选 `/etc/passwd` 里第一个 uid ≥ 1000 的普通用户。

程序运行时会写的东西都在用户家目录，不受影响：

| 内容 | 位置 |
|---|---|
| Unity 设置 / 存档 | `~/.config/unity3d/<公司>/<产品>/` |
| 启动日志 | `~/.local/state/<包名>/launch.log` |
| 自启动开关 | `~/.config/autostart/<包名>.desktop` |

#### 别用 sudo 启动

`sudo <包名>` 会把上面这些文件全写成 **root 属主**，之后普通用户再双击就写不进去 ——
典型症状是设置不保存、存档丢失、或者直接白屏。启动器检测到 root 运行时会打印一行提示，
并记进 `launch.log`。

已经这样跑过了，按下面清理（把残留删掉让它重新生成，**会丢掉那次运行产生的设置**）：

```bash
sudo rm -rf ~/.config/unity3d/*/<产品名>
rm -f /run/user/$(id -u)/<包名>.lock
```

如果那台机器只允许 root 登录（部分政企定制系统就是这样），那就保持 root 运行，
但要保证**始终**用 root，不要 root 和普通用户混着跑。

### 卸载

```bash
sudo dpkg -r <包名>      # 保留 /opt 下的用户数据
sudo dpkg -P <包名>      # 彻底清理（含自启动项、质量/接口设置、日志）
```

`postrm` 会顺手清掉各用户家目录里的快捷方式、自启动项和运行日志，以及
`/etc/<包名>/` 下的 `quality` / `graphics`（后者留着会让重装后的机器在没有 Vulkan 驱动时起不来）。
旧版本用过的 `/etc/<包名>/vulkan` 也会一并清掉。

---

## 4. 出新版本怎么打包

**复制一份配置，改 4 个字段即可**：

```bash
cp appConfigs/example.conf appConfigs/myapp-v2.conf
vi appConfigs/myapp-v2.conf
```

```ini
APP_TITLE="我的应用 v2"          # 显示名（会出现在桌面图标下）
PKG_NAME="my-application-v2"     # 包名，全小写；不要和旧版重复，否则会互相覆盖
VERSION="2.0.0"
SRC_DIR="/mnt/e/.../Releases/Linux/build"    # 新版程序目录
EXECUTABLE="MyApplication.x86_64"            # 新版的可执行文件名
LOGO="logo.jpg"                              # 目录里的 logo，写相对路径即可
```

```bash
./debkit.sh -c appConfigs/myapp-v2.conf
```

> 想让新版覆盖旧版（同一个包升级），就把 `PKG_NAME` 设成和旧版相同、只提高 `VERSION`，
> `dpkg -i` 就是原地升级，用户设置和桌面图标都会保留。

只需要重打同一版本时：`./debkit.sh -c appConfigs/xxx.conf -r 2`（修订号 +1）。

---

## 5. 配置字段速查

完整带注释的示例见 [`appConfigs/example.conf`](appConfigs/example.conf)。

| 字段 | 说明 |
|------|------|
| `APP_TITLE` | 显示名，可中文 |
| `PKG_NAME` | Debian 包名，也是启动命令名 |
| `VERSION` / `REVISION` | 版本，最终为 `VERSION-REVISION` |
| `MAINTAINER` | 维护人（真实邮箱，`dpkg -I` 会显示） |
| `HOMEPAGE` | 主页，留空则 control 里不输出该字段（内网部署常用） |
| `SECTION` / `PRIORITY` / `ARCH` | Debian 元数据，一般不用改 |
| `DESC_SHORT` / `DESC_LONG` | 简介 / 详细说明（支持多行，空行会转成 ` .`） |
| `SRC_DIR` | 程序目录 |
| `EXECUTABLE` | 目录里的主可执行文件名（会自动补 `+x`） |
| `LOGO` | 图标源图，相对 `SRC_DIR` 或绝对路径 |
| `INSTALL_DIR` | 安装到哪，默认 `/opt/<包名>` |
| `CATEGORIES` / `KEYWORDS` / `GENERIC_NAME` | 菜单分类与搜索关键字 |
| `EXTRA_ARGS` | **默认启动参数**，四个入口统一生效。见 5.1 |
| `QUALITY_DEFAULT` | 默认画质**索引**（数字，0 起）；`none` = 不生成 `/etc/<包名>/quality` |
| `GRAPHICS_DEFAULT` | 默认图形接口 `opengl` / `vulkan` / `none`；`none` = 不生成 `/etc/<包名>/graphics` |
| `AUTOSTART_DEFAULT` / `AUTOSTART_DELAY` | 是否默认自启 / 延迟秒数 |
| `CREATE_DESKTOP_ICON` | 装包时是否自动给本地用户建桌面快捷方式（默认 `yes`） |
| `WRITABLE_DIRS` | 需要交给运行用户写的安装目录子目录，空格分隔 |
| `DEPENDS` | 依赖库，逗号分隔，`|` 表示二选一 |
| `COMPRESS` / `COMPRESS_LEVEL` | 压缩方式与级别。默认 `gzip` / `6`；可选 `pigz`(最快)、`xz`(最小但慢)、`zstd`(需 dpkg≥1.21)、`auto`(自动挑)、`none` |
| `EXCLUDE` | 要从包里剔除的通配符，如 `"*.pdb *.log"` |

### 5.1 启动参数（`EXTRA_ARGS`）改在哪

生效链路只有一条，**都写在启动器 `/usr/bin/<包名>` 里**：

```
桌面图标 ┐
开始菜单 ├─→ /usr/bin/<包名>  ──→ 读取 EXTRA_ARGS ──→ exec /opt/<包名>/<主程序> [参数]
开机自启 ┤        ↑
终端敲命令 ┘   这里统一注入(还会拼上 -picture-quality / -force-*)
```

**打包前改**（推荐，一次到位）：编辑 `appConfigs/<包名>.conf` 的 `EXTRA_ARGS`，例如全屏启动：

```ini
EXTRA_ARGS="-screen-fullscreen 1"
```

- 按空格拆分成多个参数
- 支持 `$HOME` 这类变量，**运行时按当前用户展开**（不是打包时）
- 含空格的单个参数用双引号包住：`-logFile "$HOME/我的 日志.log"`
- 打包时写进启动器，各入口一致

**打包后改**（不想重打大包）：

| 想改的范围 | 做法 | 升级后 |
|---|---|---|
| 只改**桌面图标** | 直接改 `~/桌面/<包名>.desktop` 的 `Exec=`，参数写在命令后面 | 保留 |
| 只改**开始菜单** | `cp /usr/share/applications/<包名>.desktop ~/.local/share/applications/` 后改其中 `Exec=` | 保留（用户级优先） |
| 只改**开机自启** | 改 `~/.config/autostart/<包名>.desktop` 的 `Exec=` | 保留 |
| **所有入口** | `sudo vi /usr/bin/<包名>`，改 `EXTRA_ARGS="..."` 那一行 | **被覆盖**，需重打才持久 |
| 画质 / 图形接口 | `<包名>-ctl quality` / `<包名>-ctl graphics`（写 `/etc/<包名>/`） | 保留 |

注意桌面快捷方式是安装时**复制**的独立副本（不是指向 `/usr/share/applications` 的链接），
所以改了开始菜单那份不会影响桌面上那份，反之亦然。

临时一次性调参不用改文件，直接追加在命令后面——**命令行给了参数就忽略默认值**：

```bash
<包名> -screen-fullscreen 0 -screen-width 1280
```

**`--autostart` 是启动器自己的内部开关**，不要写进 `EXTRA_ARGS`；它只负责「等桌面就绪再启动」，
不会被传给程序。

改完想确认实际生效的参数，看日志：

```bash
grep 'args=' ~/.local/state/<包名>/launch.log | tail -3
# args=-picture-quality 4 -force-glcore -screen-fullscreen 1
```

---

## 6. 工具做了什么（为什么不是简单 tar 一下）

1. **权限归一化**：源目录常见 `666/777`，装进系统后统一成 目录 `755` / 文件 `644` /
   可执行与 `.so` `755`，属主 `root:root`。
2. **图标自动处理**：`logo.jpg` 缩放成 8 种尺寸；方形白底会自动 floodfill 成透明
   （只处理四角连通的白色，不会误伤图形内部的白色），深色主题下也好看。
3. **桌面快捷方式**：`postinst` 遍历所有本地用户，按各大发行版的 XDG 配置
   （含中文「桌面」目录）投放到位、改好属主、补 `+x`（可用 `CREATE_DESKTOP_ICON=no` 关掉这一步）。
4. **自启动**：系统级 + 用户级双层，可按用户开关，升级不会重置用户的选择。
5. **运行时开关**：画质档位与图形接口落到 `/etc/<包名>/`，启动器每次启动读取并按需拼参数；
   升级**只在文件不存在时**写默认值，不会覆盖现场已经改过的选择。
6. **打包前自检**：渲染出的每个脚本都过一遍 `sh -n` 语法检查；打完包再核对
   `dpkg -I` 元数据与关键文件是否齐全。
7. **大包友好**：`dpkg-deb` 的临时目录固定在工作盘上（有的机器 `/tmp` 只有 10 MB tmpfs，
   默认设置会直接失败）；压缩阶段有进度条（读 `/proc/<pid>/rchar` 估算），不会看起来像卡死。

---

## 7. 常见问题

**Q: 提示 `dpkg-deb: 正在压缩 tar 成员: 写入错误: 设备上没有空间`**
`/tmp` 空间不够。工具已自动把 `TMPDIR` 指到 `work/`，若你自定义了 `-w`，
确保该分区有 **源体积 + deb 体积** 的空闲空间。

**Q: 脚本提示权限不够、无法执行**
部分系统的安全策略会限制某些目录下的脚本执行。把 `debkit` 整个目录复制到
`/usr/local/share/debkit` 或 `/tmp` 下再执行即可。（WSL 构建时不会遇到。）

**Q: 在 WSL 里打完包，装到目标机可执行位是 `0777`**
暂存目录落在了 `/mnt/<盘>` 上，drvfs 不支持 Unix 权限，`chmod` 是空操作。
加 `-w ~/debkit-work` 让暂存走 ext4。

**Q: `./debkit.sh` 报 `bad interpreter: /usr/bin/env bash^M`**
脚本被存成了 CRLF。仓库 `.gitattributes` 已对 `*.sh` / `*.in` 强制 `eol=lf`；
如果是自己从 Windows 拷过来的，用 `sed -i 's/\r$//' debkit.sh lib/*.sh templates/*.in` 修一遍。

**Q: 刷屏 `chown: 正在更改'...' 的所有者: 不允许的操作`**
1.0.1 已修复。原因有两层：`chown -R 0:0` 在**普通用户**下必然被内核拒绝（改属主只有 root 能做），
而脚本开头是 `set -Eeuo pipefail`，一条命令失败就整体中止，所以看起来像"打了一半崩了"。
现在属主靠 `dpkg-deb --root-owner-group` 保证，非 root 运行时直接跳过 `chown` 并打印一行说明。

**Q: 普通用户能把包装到哪**
工具会自动退到 `~/.local/share/debkit/{work,output}` 并打印提示；
也可以用 `-w` / `-o` 或环境变量 `DEBKIT_WORK` / `DEBKIT_OUT` 指定。

**Q: 安装时提示依赖不满足**
系统版本差异导致某个库名不存在。把 `appConfigs/*.conf` 里的 `DEPENDS` 精简成
`libc6, libstdc++6` 后重打；图形库缺失的机器上大概率本来就跑不起来。
Ubuntu 24.04 把 `libasound2` 改名成了 `libasound2t64`，示例配置里用 `libasound2 | libasound2t64`
这种二选一写法兼容两代系统。

**Q: 桌面图标显示成白底方块**
logo 不是方形，或四角不是纯白。换一张正方形、纯白底的图；
图标生成那一行会打印 `透明化处理失败, 退化为直接缩放`。

**Q: 双击桌面图标没反应**
看 `~/.local/state/<包名>/launch.log`。若显示 `Permission denied`，
确认 `/opt/<包名>/<可执行文件>` 有 `+x`（工具会自动加，手工拷文件才会丢）。

**Q: 画质切了没反应**
`<包名>-ctl quality status` 确认文件内容；再 `grep 'args=' ~/.local/state/<包名>/launch.log`
看 `-picture-quality` 有没有拼上。抽不到说明命令行里已经带了 `-picture-quality`（命令行优先），
或者主程序没实现该参数解析（本仓库的 `ProcedureLaunch` 已实现，越界会打 warning）。

**Q: 打包要多久 / 该选哪种压缩**

实测一个 Unity 工程源目录：

| 配置 | 包体积 | 压缩耗时 | 适用 |
|---|---|---|---|
| `gzip` / `6`（**默认**） | 基准 | 约 1.5 分钟 | 日常出包，体积够用 |
| `pigz` / `6` | 同 gzip | 约 20 秒 | **推荐**，和 gzip 完全同格式，只是多线程 |
| `auto` | 同上（优先 pigz） | 约 20 秒 | 换机器不用改配置 |
| `xz` / `1` | 约小 13% | 约 7 分钟 | 要最小体积、或走网络分发 |
| `zstd` / `6` | 略小于 gzip | 约 40 秒 | 反复调试时最省时间 |

体积压不下去是因为 `.bundle`/`.resS` 本身已经是压缩数据，多花的 CPU 收益有限。

**zstd 支不支持看 dpkg 版本**：`-Zzstd` 是 dpkg 1.21 才有的，旧版 dpkg 用不了。工具不会盲试 —— 配置写 `zstd` 而本机不支持时会自动降级到 gzip
并打印提示；`--check` 会直接告诉你本机支持哪几种：

```
 [ OK ] compress         可用: gzip xz zstd (默认 gzip -6)
 [ OK ] compress         可用: gzip xz (本机 dpkg 无 zstd, 会自动降级)
```

包超过 2 GB 时工具会自动跳过逐文件清单校验（那一步要把整个包解压一遍），
需要时加 `--verify`。
