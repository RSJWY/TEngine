"""纯命令行模式（--no-gui）：跑一次性构建，输出日志到 stdout。"""

from __future__ import annotations

import argparse
import json
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
                                "syncAotManifest", "copyAotDll", "switchPlatform", "buildInstaller"])
    run_p.add_argument("--target", default=None, help="目标平台（默认取 .asset 或 StandaloneWindows64）")
    run_p.add_argument("--version", default=None, help="统一版本号（Unified 模式）")
    run_p.add_argument("--output-root", default=None, help="AB 输出根目录")
    run_p.add_argument("--package", default=None, help="只构建指定资源包（默认全部）")
    run_p.add_argument("--preset", default=None, help="预设名（BuildCLI/presets 下的 json）")
    run_p.add_argument("--unity-exe", default=None, help="Unity 可执行文件路径")
    run_p.add_argument("--project", default=None, help="Unity 工程目录（默认自动推导 / 会话记录）")
    run_p.add_argument("--dry-run", action="store_true", help="只打印将要下发的请求 JSON，不启动 Unity")
    run_p.add_argument("--json", action="store_true", help="结果以 JSON 输出到 stdout（机器可读）")

    preset_p = sub.add_parser("preset", help="列出预设")
    preset_p.add_argument("--list", action="store_true")

    versions_p = sub.add_parser("list-versions", help="列出各资源包已有构建版本（扫描输出目录）")
    versions_p.add_argument("--target", default=None, help="目标平台（默认取 .asset 或会话记录）")
    versions_p.add_argument("--output-root", default=None, help="AB 输出根目录")
    versions_p.add_argument("--project", default=None, help="Unity 工程目录")
    versions_p.add_argument("--json", action="store_true", help="以 JSON 输出")

    return parser


def _assemble_state(args) -> tuple[BuildFormState | None, int]:
    """组装表单：预设 → 会话 → .asset。返回 (state, exit_code)，code!=0 时 state 无效。"""
    state: BuildFormState | None = None
    if getattr(args, "preset", None):
        path = config_store.PRESETS_DIR / f"{args.preset}.json"
        if not path.exists():
            print(f"预设不存在：{path}")
            return None, 1
        state = config_store.load_preset(path)
    if state is None:
        state = config_store.load_last_session() or config_store.load_form_from_build_pipeline_setting() or BuildFormState()

    state.action = args.action
    if args.project:
        state.projectDir = args.project
    if not state.projectDir or not (Path(state.projectDir) / "Assets").is_dir():
        print(f"项目目录无效：{state.projectDir or '<空>'}（用 --project 指定）")
        return None, 1
    if args.target:
        state.buildTarget = args.target
    if args.version:
        state.packageVersion = args.version
    if args.output_root:
        state.outputRoot = args.output_root
    if getattr(args, "package", None) is not None:
        state.packageName = args.package
    return state, 0


def _cmd_list_versions(args) -> int:
    state: BuildFormState | None = BuildFormState()
    project = args.project or (state.projectDir if state else "")
    if project and Path(project).is_dir():
        state.projectDir = project
    target = args.target or state.buildTarget
    output_root = args.output_root or state.outputRoot
    project_dir = Path(state.projectDir) if state.projectDir else unity_locator.PROJECT_DIR

    versions = config_store.read_last_package_versions(target, output_root, project_dir)
    # read_last_package_versions 只返回每包最新版本；这里扫全部版本目录
    from .unity_locator import platform_dir_name

    base = Path(output_root) if output_root and Path(output_root).is_absolute() else project_dir / (output_root or "Releases/Bundles").lstrip("./")
    platform_root = base / platform_dir_name(target)

    all_versions: dict[str, list[str]] = {}
    if platform_root.is_dir():
        for package_dir in sorted(platform_root.iterdir()):
            if not package_dir.is_dir():
                continue
            names = sorted(
                (v.name for v in package_dir.iterdir() if v.is_dir() and v.name != "OutputCache"),
                reverse=True,
            )
            if names:
                all_versions[package_dir.name] = names

    if args.json:
        print(json.dumps({"target": target, "platformDir": platform_root.as_posix(),
                          "packages": all_versions}, ensure_ascii=False, indent=2))
    else:
        print(f"平台：{target}  目录：{platform_root}")
        if not all_versions:
            print("（无构建记录）")
        for name, vers in all_versions.items():
            latest = versions.get(name, vers[0])
            print(f"  {name}: {len(vers)} 个版本，最新 {latest}")
            for v in vers:
                marker = " ←最新" if v == latest else ""
                print(f"    {v}{marker}")
    return 0


def run_cli(argv: list[str]) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "preset":
        for p in config_store.list_presets():
            print(p.stem)
        return 0

    if args.command == "list-versions":
        return _cmd_list_versions(args)

    if args.command != "run":
        parser.print_help()
        return 0

    state, code = _assemble_state(args)
    if state is None:
        return code

    if args.dry_run:
        payload = {k: v for k, v in vars(state).items()
                   if k not in {"unityExePath", "projectDir", "logKeepCount", "logKeepDays", "buildTimeoutMinutes"}}
        print(json.dumps({"action": state.action, "projectDir": state.projectDir, "request": payload},
                         ensure_ascii=False, indent=2, default=str))
        return 0

    unity_exe = unity_locator.locate_unity_exe(
        custom_path=args.unity_exe or state.unityExePath,
        preferred_version=unity_locator.read_project_unity_version(project_dir=Path(state.projectDir)),
    )
    if unity_exe is None:
        print("未找到 Unity.exe，用 --unity-exe 指定。")
        return 1
    state.unityExePath = str(unity_exe)

    run = BuildRun(state, Path(unity_exe))
    if not args.json:
        run.on_log(print)
    if not run.start():
        if args.json:
            _print_json_result(run)
        return 1

    try:
        while run.pump():
            time.sleep(0.5)
    except KeyboardInterrupt:
        run.cancel()
        if not args.json:
            print("\n[BuildCLI] 已取消。")
        return 130

    if args.json:
        _print_json_result(run)
    else:
        print(f"[BuildCLI] 结果：{run.result} | 日志目录：{run.log_dir}")
    return 0 if run.result == "success" else 1


def _print_json_result(run: BuildRun) -> None:
    payload: dict = {
        "result": run.result,
        "logDir": str(run.log_dir),
    }
    if run.failure_summary:
        payload["failureSummary"] = run.failure_summary
    if run.unity_result:
        payload["unityResult"] = run.unity_result
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    sys.exit(run_cli(sys.argv[1:]))
