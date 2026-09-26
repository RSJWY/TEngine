"""构建配置存取：预设 JSON 读写 + Unity 侧 .asset 只读解析。

对 Unity 资产只做只读解析（手写迷你 UnityYAML 解析，避免 ruamel/pyyaml 依赖与
锚点流问题），写入统一走 JSON 预设，绝不直接改 .asset。
"""

from __future__ import annotations

import copy
import json
import re
from dataclasses import asdict, dataclass, field
from pathlib import Path

from . import unity_locator

PRESETS_DIR = unity_locator.REPO_ROOT / "BuildCLI" / "presets"
LAST_SESSION_PATH = PRESETS_DIR / "_last_session.json"


def _project_settings_dir(project_dir: Path | None = None) -> Path:
    return (project_dir or unity_locator.PROJECT_DIR) / "Assets" / "TEngine" / "Settings"


def build_pipeline_setting_asset(project_dir: Path | None = None) -> Path:
    return _project_settings_dir(project_dir) / "BuildPipelineSetting.asset"


def update_setting_asset(project_dir: Path | None = None) -> Path:
    return _project_settings_dir(project_dir) / "UpdateSetting.asset"

# BuildTarget 枚举值（Unity 2022.3）：用于 .asset 中数字 → 名称
BUILD_TARGET_NAMES = {
    2: "StandaloneWindows64",   # 实际 NoTarget=1, StandaloneWindows64=19，见下
    19: "StandaloneWindows64",
    8: "StandaloneLinux64",
    9: "StandaloneOSX",
    13: "Android",
    24: "Windows",  # 兼容占位
    4: "Android",
    25: "WebGL",
    11: "iOS",
}


@dataclass
class PackageVersionEntry:
    packageName: str = ""
    version: str = ""


@dataclass
class BuildFormState:
    """GUI 表单状态，字段名与 CLIBridge.BuildRequestDTO 一一对应。"""

    # 环境类（不入预设核心，但随预设保存）
    unityExePath: str = ""
    projectDir: str = ""
    # 日志自动清理策略：保留最近 N 次；超过 M 天的删除（满足任一即删；0 表示不限）
    logKeepCount: int = 30
    logKeepDays: int = 7
    # 动作
    action: str = "build"
    # 基础设置
    buildTarget: str = "StandaloneWindows64"
    buildPipeline: str = "ScriptableBuildPipeline"
    compressOption: str = "LZ4"
    packageVersion: str = ""
    packageVersionMode: str = "Unified"
    packageVersions: list[PackageVersionEntry] = field(default_factory=list)
    outputRoot: str = "./Releases/Bundles/"
    # 发布整理
    enablePublishCopy: bool = False
    publishRoot: str = "./Releases/Publish/"
    cleanPublishPackageDirectory: bool = True
    # 最小包
    minimalPackage: bool = False
    retainTags: str = ""
    # 高级
    enableSharePackRule: bool = True
    enableAssetPathValidation: bool = True
    useAssetDependencyDB: bool = True
    clearBuildCache: bool = False
    verifyBuildingResult: bool = True
    buildinFileCopyOption: str = "ClearAndCopyAll"
    fileNameStyle: str = "BundleName_HashName"
    generateCatalogInOutput: bool = False
    # 热更 DLL
    buildHotFixDll: bool = True
    # Player
    buildPlayer: bool = False
    playerPlatform: str = "StandaloneWindows64"
    playerOutputPath: str = ""


# ============ Unity YAML 只读解析 ============

def _parse_unity_yaml_scalar(text: str) -> object:
    text = text.strip()
    if text.startswith('"') and text.endswith('"'):
        return text[1:-1]
    if text in ("", "null", "~"):
        return ""
    if text in ("true", "True"):
        return True
    if text in ("false", "False"):
        return False
    if re.fullmatch(r"-?\d+", text):
        return int(text)
    if re.fullmatch(r"-?\d+\.\d+(e[+-]?\d+)?", text, re.IGNORECASE):
        return float(text)
    return text


def parse_unity_asset_fields(asset_path: Path) -> dict[str, object]:
    """极简解析 Unity .asset（MonoBehaviour 单文档）顶层标量字段。"""
    if not asset_path.exists():
        return {}

    result: dict[str, object] = {}
    in_mono_behaviour = False
    try:
        lines = asset_path.read_text(encoding="utf-8").splitlines()
    except OSError:
        return {}

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("MonoBehaviour:"):
            in_mono_behaviour = True
            continue
        if not in_mono_behaviour:
            continue
        if stripped.startswith("---"):  # 文档边界
            if result:  # 已收集到 MonoBehaviour 段则停止
                break
            continue
        if not stripped or stripped.startswith("#"):
            continue
        if line.startswith("  ") and not line.startswith("   ") and ":" in line:
            key, _, raw_value = line[2:].partition(":")
            if key.startswith("m_"):
                continue
            result[key.strip()] = _parse_unity_yaml_scalar(raw_value)
    return result


def load_runtime_package_names(project_dir: Path | None = None) -> list[str]:
    """从 UpdateSetting.asset 解析启用的运行时资源包名（只读）。"""
    text = _read_asset_text(update_setting_asset(project_dir))
    if not text:
        return ["DefaultPackage"]

    names: list[str] = []
    current: dict[str, object] = {}
    in_packages = False
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("RuntimePackages:"):
            in_packages = True
            continue
        if in_packages:
            if line.startswith("  - "):  # 新条目
                if current.get("Enable") and current.get("PackageName"):
                    names.append(str(current["PackageName"]))
                current = {}
                kv = stripped[2:]
                if ":" in kv:
                    key, _, value = kv.partition(":")
                    current[key.strip()] = _parse_unity_yaml_scalar(value)
            elif line.startswith("    ") and ":" in stripped:
                key, _, value = stripped.partition(":")
                current[key.strip()] = _parse_unity_yaml_scalar(value)
            elif line and not line.startswith(" "):  # 回到顶层字段
                if current.get("Enable") and current.get("PackageName"):
                    names.append(str(current["PackageName"]))
                in_packages = False
    if current.get("Enable") and current.get("PackageName"):
        names.append(str(current["PackageName"]))

    if not names:
        names = ["DefaultPackage"]
    return names


def _read_asset_text(path: Path) -> str | None:
    if not path.exists():
        return None
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return None


def load_form_from_build_pipeline_setting(project_dir: Path | None = None) -> BuildFormState | None:
    """把 Unity 打包工具窗口当前配置（BuildPipelineSetting.asset）读为表单起点。只读。"""
    fields = parse_unity_asset_fields(build_pipeline_setting_asset(project_dir))
    if not fields:
        return None

    state = BuildFormState()
    state.buildTarget = _map_build_target(fields.get("BuildTarget"))
    state.buildPipeline = _map_enum_name(fields.get("BuildPipeline"), "ScriptableBuildPipeline")
    state.compressOption = _map_enum_name(fields.get("CompressOption"), "LZ4")
    state.packageVersion = str(fields.get("PackageVersion", "") or "")
    state.packageVersionMode = "PerPackage" if fields.get("PackageVersionMode") in (1, "PerPackage") else "Unified"
    state.outputRoot = str(fields.get("OutputRoot", "") or "./Releases/Bundles/")
    state.enablePublishCopy = bool(fields.get("EnablePublishCopy", False))
    state.publishRoot = str(fields.get("PublishRoot", "") or "./Releases/Publish/")
    state.cleanPublishPackageDirectory = bool(fields.get("CleanPublishPackageDirectory", True))
    state.minimalPackage = bool(fields.get("MinimalPackage", False))
    state.retainTags = str(fields.get("RetainTags", "") or "")
    state.enableSharePackRule = bool(fields.get("EnableSharePackRule", True))
    state.enableAssetPathValidation = bool(fields.get("EnableAssetPathValidation", True))
    state.useAssetDependencyDB = bool(fields.get("UseAssetDependencyDB", True))
    state.clearBuildCache = bool(fields.get("ClearBuildCache", False))
    state.verifyBuildingResult = bool(fields.get("VerifyBuildingResult", True))
    state.buildinFileCopyOption = _map_enum_name(fields.get("BuildinFileCopyOption"), "ClearAndCopyAll")
    state.fileNameStyle = _map_enum_name(fields.get("FileNameStyle"), "BundleName_HashName")
    state.generateCatalogInOutput = bool(fields.get("GenerateCatalogInOutput", False))
    state.buildHotFixDll = bool(fields.get("BuildHotFixDll", True))
    state.buildPlayer = bool(fields.get("BuildPlayer", False))
    state.playerPlatform = _map_build_target(fields.get("PlayerPlatform"))
    state.playerOutputPath = str(fields.get("PlayerOutputPath", "") or "")
    return state


def _map_build_target(value: object) -> str:
    if isinstance(value, int):
        return BUILD_TARGET_NAMES.get(value, "StandaloneWindows64")
    if isinstance(value, str) and value in unity_locator.SUPPORTED_BUILD_TARGETS:
        return value
    return "StandaloneWindows64"


def _map_enum_name(value: object, fallback: str) -> str:
    if isinstance(value, str) and value:
        return value
    return fallback


# ============ 版本号读取（与 ReleaseTools.GetPackageVersionDirectories 语义一致） ============

def read_last_package_versions(build_target: str, output_root: str, project_dir: Path | None = None) -> dict[str, str]:
    """扫描 Bundles/<平台>/<包名>/ 下最新版本目录名。返回 包名→版本号。"""
    from .unity_locator import platform_dir_name

    project = project_dir or unity_locator.PROJECT_DIR
    if output_root and not Path(output_root).is_absolute():
        base = project / output_root.lstrip("./")
    else:
        base = Path(output_root or (project / "Releases/Bundles"))

    platform_root = Path(base) / platform_dir_name(build_target)
    result: dict[str, str] = {}
    if not platform_root.is_dir():
        return result

    for package_dir in platform_root.iterdir():
        if not package_dir.is_dir():
            continue
        versions: list[tuple[float, str]] = []
        for version_dir in package_dir.iterdir():
            if not version_dir.is_dir() or version_dir.name == "OutputCache":
                continue
            try:
                versions.append((version_dir.stat().st_mtime, version_dir.name))
            except OSError:
                continue
        if versions:
            versions.sort(reverse=True)
            result[package_dir.name] = versions[0][1]
    return result


def default_package_version() -> str:
    """与 BuildConfig.GetDefaultPackageVersion 一致：yyyy-MM-dd-当日分钟数。"""
    import datetime

    now = datetime.datetime.now()
    return f"{now.strftime('%Y-%m-%d')}-{now.hour * 60 + now.minute}"


# ============ 预设/会话持久化 ============

def form_to_json(state: BuildFormState) -> str:
    data = asdict(state)
    return json.dumps(data, ensure_ascii=False, indent=2)


def form_from_json(text: str) -> BuildFormState:
    data = json.loads(text)
    versions = data.pop("packageVersions", [])
    state = BuildFormState(**{k: v for k, v in data.items() if k in BuildFormState.__dataclass_fields__})
    state.packageVersions = [PackageVersionEntry(**v) for v in versions if isinstance(v, dict)]
    return state


def save_preset(name: str, state: BuildFormState) -> Path:
    PRESETS_DIR.mkdir(parents=True, exist_ok=True)
    safe = re.sub(r'[\\/:*?"<>|]', "_", name.strip()) or "preset"
    path = PRESETS_DIR / f"{safe}.json"
    path.write_text(form_to_json(state), encoding="utf-8")
    return path


def load_preset(path: Path) -> BuildFormState:
    return form_from_json(path.read_text(encoding="utf-8"))


def list_presets() -> list[Path]:
    if not PRESETS_DIR.is_dir():
        return []
    return sorted(p for p in PRESETS_DIR.glob("*.json") if not p.name.startswith("_"))


def save_last_session(state: BuildFormState) -> None:
    PRESETS_DIR.mkdir(parents=True, exist_ok=True)
    LAST_SESSION_PATH.write_text(form_to_json(state), encoding="utf-8")


def load_last_session() -> BuildFormState | None:
    if not LAST_SESSION_PATH.exists():
        return None
    try:
        return form_from_json(LAST_SESSION_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None


def default_project_dir(state: BuildFormState | None = None) -> str:
    """表单 projectDir 的兜底值：优先表单现值，其次自动推导（BuildCLI 的上级目录）。"""
    if state and state.projectDir and Path(state.projectDir).is_dir():
        return state.projectDir
    return str(unity_locator.PROJECT_DIR)


def clone_state(state: BuildFormState) -> BuildFormState:
    return copy.deepcopy(state)
