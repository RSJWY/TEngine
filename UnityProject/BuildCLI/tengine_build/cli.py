"""纯命令行模式（--no-gui）：跑一次性构建，输出日志到 stdout。"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

from . import config_store, unity_locator
from .config_store import BuildFormState
from .unity_runner import BuildRun


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="tengine_build", description="TEngine 命令行构建工具")
    sub = parser.add_subparsers(dest="command")

    run_p = sub.add_parser("run", help="执行一次构建（默认 action=build）")
    run_p.add_argument("--action", default="build",
                       choices=["build", "buildAb", "buildPlayer", "publish", "hotfixDll", "generateAll",
                                "syncAotManifest", "copyAotDll", "switchPlatform"])
    run_p.add_argument("--target", default=None, help="目标平台（默认取 .asset 或 StandaloneWindows64）")
    run_p.add_argument("--version", default=None, help="统一版本号（Unified 模式）")
    run_p.add_argument("--output-root", default=None, help="AB 输出根目录")
    run_p.add_argument("--preset", default=None, help="预设名（BuildCLI/presets 下的 json）")
    run_p.add_argument("--unity-exe", default=None, help="Unity 可执行文件路径")
    run_p.add_argument("--project", default=None, help="Unity 工程目录（默认自动推导 / 会话记录）")

    preset_p = sub.add_parser("preset", help="列出/保存预设")
    preset_p.add_argument("--list", action="store_true")

    return parser


def run_cli(argv: list[str]) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "preset":
        for p in config_store.list_presets():
            print(p.stem)
        return 0

    if args.command != "run":
        parser.print_help()
        return 0

    # 组装表单
    state: BuildFormState | None = None
    if args.preset:
        path = config_store.PRESETS_DIR / f"{args.preset}.json"
        if not path.exists():
            print(f"预设不存在：{path}")
            return 1
        state = config_store.load_preset(path)
    if state is None:
        state = config_store.load_last_session() or config_store.load_form_from_build_pipeline_setting() or BuildFormState()

    state.action = args.action
    if args.project:
        state.projectDir = args.project
    if not state.projectDir or not (Path(state.projectDir) / "Assets").is_dir():
        print(f"项目目录无效：{state.projectDir or '<空>'}（用 --project 指定）")
        return 1
    if args.target:
        state.buildTarget = args.target
    if args.version:
        state.packageVersion = args.version
    if args.output_root:
        state.outputRoot = args.output_root

    unity_exe = unity_locator.locate_unity_exe(
        custom_path=args.unity_exe or state.unityExePath,
        preferred_version=unity_locator.read_project_unity_version(project_dir=Path(state.projectDir)),
    )
    if unity_exe is None:
        print("未找到 Unity.exe，用 --unity-exe 指定。")
        return 1
    state.unityExePath = str(unity_exe)

    run = BuildRun(state, Path(unity_exe))
    run.on_log(print)
    if not run.start():
        return 1

    try:
        while run.pump():
            time.sleep(0.5)
    except KeyboardInterrupt:
        run.cancel()
        print("\n[BuildCLI] 已取消。")
        return 130

    print(f"[BuildCLI] 结果：{run.result} | 日志目录：{run.log_dir}")
    return 0 if run.result == "success" else 1


if __name__ == "__main__":
    sys.exit(run_cli(sys.argv[1:]))
