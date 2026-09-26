"""仓库/项目路径解析与 Unity 编辑器定位。"""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

# BuildCLI/tengine_build/ → UnityProject（项目根，CLI 工具随项目走）
REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT_DIR = REPO_ROOT
UNITYPROJECT_SETTINGS_DIR = PROJECT_DIR / "ProjectSettings"

PLATFORM_DIR_NAMES = {
    "StandaloneWindows64": "Windows64",
    "StandaloneWindows64 ": "Windows64",
    "StandaloneOSX": "MacOS",
    "StandaloneLinux64": "Linux",
    "Android": "Android",
    "iOS": "IOS",
    "WebGL": "WebGL",
}

SUPPORTED_BUILD_TARGETS = [
    "StandaloneWindows64",
    "StandaloneOSX",
    "StandaloneLinux64",
    "Android",
    "iOS",
    "WebGL",
]

BUILD_PIPELINES = ["ScriptableBuildPipeline", "BuiltinBuildPipeline", "RawFileBuildPipeline", "ArchiveFileBuildPipeline"]
COMPRESS_OPTIONS = ["Uncompressed", "LZMA", "LZ4"]
BUNDLED_COPY_OPTIONS = ["None", "ClearAndCopyAll", "ClearAndCopyByTags", "OnlyCopyAll", "OnlyCopyByTags"]
FILE_NAME_STYLES = ["None", "HashName", "BundleName_HashName", "BundleName_HashName2"]


def platform_dir_name(build_target: str) -> str:
    return PLATFORM_DIR_NAMES.get(build_target, build_target)


def read_project_unity_version(project_dir: Path | None = None) -> str | None:
    settings_dir = (project_dir or PROJECT_DIR) / "ProjectSettings"
    version_file = settings_dir / "ProjectVersion.txt"
    if not version_file.exists():
        return None
    match = re.search(r"m_EditorVersion:\s*(\S+)", version_file.read_text(encoding="utf-8"))
    return match.group(1) if match else None


def _unity_hub_editors_dir() -> Path | None:
    if os.name != "nt":
        hub = Path.home() / "Applications" / "Unity" / "Hub" / "Editor"
    else:
        hub = Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"
    return hub if hub.is_dir() else None


def _unity_hub_secondary_editors_dir() -> Path | None:
    """Unity Hub 的自定义安装根（从 Hub 配置文件解析，Windows: APPDATA/UnityHub/secondaryInstallPath.json）。"""
    if os.name != "nt":
        return None
    import json

    config = Path(os.environ.get("APPDATA", "")) / "UnityHub" / "secondaryInstallPath.json"
    if not config.is_file():
        return None
    try:
        data = json.loads(config.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    path = Path(str(data)) if data else None
    return path if path and path.is_dir() else None


def _unity_legacy_install_dir() -> Path | None:
    if os.name != "nt":
        return None
    legacy = Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Unity"
    return legacy if legacy.is_dir() else None


def _registry_unity_install_dirs() -> list[Path]:
    """注册表 Unity Technologies\Installer 下的安装位置。"""
    if os.name != "nt":
        return []
    import winreg

    dirs: list[Path] = []
    for root in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
        try:
            key = winreg.OpenKey(root, r"Software\Unity Technologies\Installer")
        except OSError:
            continue
        try:
            index = 0
            while True:
                try:
                    subkey_name = winreg.EnumKey(key, index)
                except OSError:
                    break
                index += 1
                try:
                    with winreg.OpenKey(key, subkey_name) as subkey:
                        value, _ = winreg.QueryValueEx(subkey, "Location x64")
                        dirs.append(Path(str(value)))
                except OSError:
                    continue
        finally:
            winreg.CloseKey(key)
    return dirs


def _common_drive_unity_dirs() -> list[Path]:
    """常见自定义安装盘符下的 Unity 根（如 E盘 Unity/Editor 目录）。"""
    if os.name != "nt":
        return []
    roots: list[Path] = []
    for drive in ("C:", "D:", "E:", "F:", "G:"):
        for pattern in ("Unity/Editor", "UnityEditor", "Unity/Hub/Editor"):
            candidate = Path(f"{drive}\\{pattern}")
            if candidate.is_dir():
                roots.append(candidate)
    return roots


def _iter_unity_exe_candidates() -> list[Path]:
    candidates: list[Path] = []
    bases: list[Path] = []
    for base in (_unity_hub_editors_dir(), _unity_hub_secondary_editors_dir(), _unity_legacy_install_dir()):
        if base is not None and base not in bases:
            bases.append(base)
    for base in _registry_unity_install_dirs() + _common_drive_unity_dirs():
        # 注册表指向的是 Unity 编辑器安装根（其下直接是 版本目录/Editor/Unity.exe）
        if base.name.lower() == "editor":
            bases.append(base)
        else:
            bases.append(base / "Editor" if base.is_dir() else base)
    seen: set[Path] = set()
    for base in bases:
        if base is None or not base.is_dir() or base in seen:
            continue
        seen.add(base)
        try:
            for editor_dir in sorted(base.iterdir(), reverse=True):
                exe = editor_dir / "Editor" / "Unity.exe" if os.name == "nt" else editor_dir / "Contents" / "MacOS" / "Unity"
                if exe.is_file() and exe not in candidates:
                    candidates.append(exe)
        except OSError:
            continue
    return candidates


def locate_unity_exe(preferred_version: str | None = None, custom_path: str | None = None) -> Path | None:
    """按 自定义路径 → 版本匹配 → 最新安装 顺序定位 Unity 可执行文件。"""
    if custom_path:
        path = Path(custom_path)
        if path.is_file():
            return path

    candidates = _iter_unity_exe_candidates()
    if not candidates:
        return None

    if preferred_version:
        for exe in candidates:
            if preferred_version in exe.as_posix():
                return exe

    return candidates[0]


def open_in_file_manager(path: str | Path) -> None:
    path = Path(path)
    target = path if path.is_dir() else path.parent
    try:
        if os.name == "nt":
            subprocess.Popen(["explorer", str(target)])
        elif sys_platform_is_darwin():
            subprocess.Popen(["open", str(target)])
        else:
            subprocess.Popen(["xdg-open", str(target)])
    except OSError:
        pass


def sys_platform_is_darwin() -> bool:
    import sys

    return sys.platform == "darwin"
