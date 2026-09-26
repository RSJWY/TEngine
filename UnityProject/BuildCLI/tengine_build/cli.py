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
    run_p.add_argument("--action", default=None, action="append",
                       choices=["build", "buildAb", "buildPlayer", "publish", "hotfixDll", "generateAll",
                                "syncAotManifest", "copyAotDll", "switchPlatform", "buildInstaller"],
                       help="要执行的动作，可重复传入按顺序执行（如 --action hotfixDll --action buildAb）；"
                            "不传时用 --batch 任务的队列，否则单动作 build")
    run_p.add_argument("--batch", default=None,
                       help="批量任务名（BuildCLI/batches 下的 json）：按任务定义的动作队列执行，"
                            "配置用其绑定预设（可再叠加 --preset/--target 等覆盖）")
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

    batch_p = sub.add_parser("batch", help="列出批量任务")
    batch_p.add_argument("--list", action="store_true")

    versions_p = sub.add_parser("list-versions", help="列出各资源包已有构建版本（扫描输出目录）")
    versions_p.add_argument("--target", default=None, help="目标平台（默认取 .asset 或会话记录）")
    versions_p.add_argument("--output-root", default=None, help="AB 输出根目录")
    versions_p.add_argument("--project", default=None, help="Unity 工程目录")
    versions_p.add_argument("--json", action="store_true", help="以 JSON 输出")

    return parser


def _assemble_state(args) -> tuple[BuildFormState | None, int]:
    """组装表单：批量任务绑定预设 → --preset → 会话 → .asset。返回 (state, exit_code)。"""
    actions: list[str] | None = None
    stop_on_failure = True
    batch_name = ""

    if getattr(args, "batch", None):
        batch = config_store.load_batch(args.batch)
        if batch is None:
            print(f"批量任务不存在：{args.batch}（查找目录：{config_store.BATCHES_DIR}）")
            return None, 1
        batch_name = batch.name or args.batch
        actions = list(batch.steps)
        stop_on_failure = batch.stopOnFailure
        # 基础表单：绑定预设存在则加载，否则回落会话/.asset
        if batch.preset:
            preset_path = config_store.PRESETS_DIR / f"{batch.preset}.json"
            if not preset_path.is_file():
                print(f"批量任务绑定的预设不存在：{preset_path}")
                return None, 1
            state = config_store.load_preset(preset_path)
        else:
            state = config_store.load_last_session() or \
                config_store.load_form_from_build_pipeline_setting() or BuildFormState()
    elif getattr(args, "preset", None):
        path = config_store.PRESETS_DIR / f"{args.preset}.json"
        if not path.exists():
            print(f"预设不存在：{path}")
            return None, 1
        state = config_store.load_preset(path)
    else:
        state = config_store.load_last_session() or \
            config_store.load_form_from_build_pipeline_setting() or BuildFormState()

    # --action 覆盖：显式传入则取代队列（批量任务的或默认单动作）
    if getattr(args, "action", None):
        actions = list(args.action)

    if actions is None or not actions:
        actions = [state.action] if state.action else ["build"]
    state.action = actions[0]
    state.batchName = batch_name

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
    return state, 0, actions, stop_on_failure


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

    if args.command == "batch":
        for p in config_store.list_batches():
            task = config_store.load_batch(p.stem)
            if task:
                steps = "->".join(task.steps) or "(空)"
                preset = f"预设={task.preset}" if task.preset else "用当前表单"
                print(f"{task.name}\t{len(task.steps)}步\t{steps}\t{preset}")
        return 0

    if args.command == "list-versions":
        return _cmd_list_versions(args)

    if args.command != "run":
        parser.print_help()
        return 0

    state, code, actions, stop_on_failure = _assemble_state(args)
    if state is None:
        return code

    if args.dry_run:
        from .dto import dump_request
        import tempfile
        tmp = Path(tempfile.gettempdir()) / "tengine_build_dryrun.json"
        req = dump_request(state, tmp.parent, actions)
        print(json.dumps({"actions": actions, "projectDir": state.projectDir,
                          "request": json.loads(req.read_text(encoding="utf-8"))},
                         ensure_ascii=False, indent=2))
        return 0

    unity_exe = unity_locator.locate_unity_exe(
        custom_path=args.unity_exe or state.unityExePath,
        preferred_version=unity_locator.read_project_unity_version(project_dir=Path(state.projectDir)),
    )
    if unity_exe is None:
        print("未找到 Unity.exe，用 --unity-exe 指定。")
        return 1
    state.unityExePath = str(unity_exe)

    # 单动作：直接 BuildRun；多动作：BatchRun 分段合并执行
    from .unity_runner import BatchRun, split_into_segments
    if len(actions) == 1 or len(split_into_segments(actions)) == 1:
        run = BuildRun(state, Path(unity_exe), actions=actions if len(actions) > 1 else None)
        if not args.json:
            run.on_log(print)
        if not run.start():
            if args.json:
                _print_json_result(run, actions)
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
            _print_json_result(run, actions)
        else:
            print(f"[BuildCLI] 结果：{run.result} | 日志目录：{run.log_dir}")
        return 0 if run.result == "success" else 1

    batch_run = BatchRun(state, Path(unity_exe), actions, stop_on_failure=stop_on_failure,
                         name=state.batchName or "")
    if not args.json:
        batch_run.on_log(print)
        batch_run.on_step(lambda idx, action, seg, total:
                          print(f"[BuildCLI] 第 {idx} 步：{action}（段 {seg}/{total}）"))
    if not batch_run.start():
        if args.json:
            _print_batch_json_result(batch_run)
        return 1
    try:
        while batch_run.pump():
            time.sleep(0.5)
    except KeyboardInterrupt:
        batch_run.cancel()
        if not args.json:
            print("\n[BuildCLI] 已取消。")
        return 130
    if args.json:
        _print_batch_json_result(batch_run)
    else:
        print(f"[BuildCLI] 结果：{batch_run.result} | 完成 {batch_run.completed_steps}/{batch_run.total_steps} 步"
              f" | 日志目录：{batch_run.run.log_dir if batch_run.run else '?'}")
    return 0 if batch_run.result == "success" else 1


def _print_json_result(run: BuildRun, actions: list[str] | None = None) -> None:
    payload: dict = {
        "result": run.result,
        "logDir": str(run.log_dir),
    }
    if actions and len(actions) > 1:
        payload["actions"] = actions
    if run.failure_summary:
        payload["failureSummary"] = run.failure_summary
    if run.unity_result:
        payload["unityResult"] = run.unity_result
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def _print_batch_json_result(batch_run: BatchRun) -> None:
    payload: dict = {
        "result": batch_run.result,
        "totalSteps": batch_run.total_steps,
        "completedSteps": batch_run.completed_steps,
        "segments": batch_run.segments,
        "logDir": str(batch_run.run.log_dir if batch_run.run else ""),
    }
    if batch_run.failure_summary:
        payload["failureSummary"] = batch_run.failure_summary
    print(json.dumps(payload, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    sys.exit(run_cli(sys.argv[1:]))
