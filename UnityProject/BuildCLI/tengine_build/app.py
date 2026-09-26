"""PySide6 GUI 主窗口。"""

from __future__ import annotations

import sys
from pathlib import Path

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QFont, QTextCursor
from PySide6.QtWidgets import (
    QApplication, QCheckBox, QComboBox, QFileDialog, QFormLayout, QGridLayout,
    QGroupBox, QHBoxLayout, QHeaderView, QLabel, QLineEdit, QMainWindow,
    QMessageBox, QPlainTextEdit, QPushButton, QTableWidget, QTableWidgetItem,
    QTabWidget, QVBoxLayout, QWidget,
)

from . import config_store, unity_locator
from .config_store import BuildFormState, PackageVersionEntry
from .unity_runner import BuildRun


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("TEngine BuildCLI 构建工具")
        self.resize(1080, 760)

        self.state = config_store.load_last_session() or config_store.load_form_from_build_pipeline_setting() or BuildFormState()
        if not self.state.projectDir or not Path(self.state.projectDir).is_dir():
            self.state.projectDir = str(unity_locator.PROJECT_DIR)
        if not self.state.unityExePath:
            exe = unity_locator.locate_unity_exe(
                preferred_version=unity_locator.read_project_unity_version(
                    project_dir=Path(self.state.projectDir)))
            self.state.unityExePath = str(exe) if exe else ""

        self.run: BuildRun | None = None
        self._build_ui()
        self._load_state_into_ui()
        self.per_package_table.itemChanged.connect(self._on_package_version_changed)
        self._refresh_package_versions(silent=True)

        self.pump_timer = QTimer(self)
        self.pump_timer.setInterval(300)
        self.pump_timer.timeout.connect(self._pump)

    # ============ UI 组装 ============

    def _build_ui(self) -> None:
        central = QWidget()
        root = QVBoxLayout(central)
        root.addWidget(self._build_top_bar())
        root.addWidget(self._build_action_bar(), stretch=0)

        body = QHBoxLayout()
        tabs = QTabWidget()
        tabs.addTab(self._build_quick_tab(), "快速构建")
        tabs.addTab(self._build_publish_tab(), "发布与Player")
        tabs.addTab(self._build_advanced_tab(), "高级")
        body.addWidget(tabs, stretch=3)
        body.addWidget(self._build_log_panel(), stretch=2)
        root.addLayout(body, stretch=1)
        self.setCentralWidget(central)

    def _build_top_bar(self) -> QWidget:
        box = QGroupBox("环境")
        layout = QGridLayout(box)

        layout.addWidget(QLabel("项目目录："), 0, 0)
        self.project_dir_edit = QLineEdit()
        self.project_dir_edit.setPlaceholderText("Unity 工程根（含 Assets/ProjectSettings）")
        layout.addWidget(self.project_dir_edit, 0, 1)
        browse_project = QPushButton("浏览")
        browse_project.clicked.connect(self._browse_project_dir)
        layout.addWidget(browse_project, 0, 2)
        self.project_dir_valid_label = QLabel("")
        layout.addWidget(self.project_dir_valid_label, 0, 3, 1, 2)
        self.project_dir_edit.textChanged.connect(self._on_project_dir_changed)

        layout.addWidget(QLabel("Unity.exe："), 1, 0)
        self.unity_exe_edit = QLineEdit()
        layout.addWidget(self.unity_exe_edit, 1, 1)
        browse_unity = QPushButton("浏览")
        browse_unity.clicked.connect(self._browse_unity)
        layout.addWidget(browse_unity, 1, 2)
        auto_unity = QPushButton("自动定位")
        auto_unity.clicked.connect(self._auto_locate_unity)
        layout.addWidget(auto_unity, 1, 3)
        self.unity_version_label = QLabel("")
        layout.addWidget(self.unity_version_label, 1, 4)

        layout.addWidget(QLabel("预设："), 2, 0)
        self.preset_combo = QComboBox()
        self.preset_combo.setMinimumWidth(220)
        layout.addWidget(self.preset_combo, 2, 1)
        load_preset = QPushButton("加载预设")
        load_preset.clicked.connect(self._load_preset)
        layout.addWidget(load_preset, 2, 2)
        save_preset = QPushButton("保存为预设")
        save_preset.clicked.connect(self._save_preset)
        layout.addWidget(save_preset, 2, 3)
        from_asset = QPushButton("从 Unity 窗口配置读取")
        from_asset.clicked.connect(self._load_from_asset)
        layout.addWidget(from_asset, 2, 4)

        self._reload_preset_combo()
        return box

    def _build_action_bar(self) -> QWidget:
        box = QGroupBox("构建操作")
        layout = QHBoxLayout(box)

        def add_button(text: str, handler, style: str = "") -> QPushButton:
            btn = QPushButton(text)
            btn.clicked.connect(handler)
            if style:
                btn.setStyleSheet(style)
            layout.addWidget(btn)
            return btn

        green = "background-color: rgba(90, 200, 120, 0.35);"
        blue = "background-color: rgba(110, 170, 235, 0.35);"
        add_button("编译并拷贝热更DLL", lambda: self._start_action("hotfixDll"))
        add_button("构建 AssetBundle", lambda: self._start_action("buildAb"), blue)
        add_button("一键构建 (AB + Player)", lambda: self._start_action("build"), green)
        add_button("仅构建 Player", lambda: self._start_action("buildPlayer"))
        add_button("仅执行发布整理", lambda: self._start_action("publish"))
        add_button("同步 AOT 元数据清单", lambda: self._start_action("syncAotManifest"))
        add_button("拷贝 AOT 元数据 DLL", lambda: self._start_action("copyAotDll"))
        add_button("GenerateAll（首包）", lambda: self._start_action("generateAll"))
        add_button("切换目标平台", lambda: self._start_action("switchPlatform"))
        layout.addStretch()

        self.cancel_button = QPushButton("取消构建")
        self.cancel_button.setEnabled(False)
        self.cancel_button.setStyleSheet("color: #d33;")
        self.cancel_button.clicked.connect(self._cancel_build)
        layout.addWidget(self.cancel_button)

        self.status_label = QLabel("就绪")
        layout.addWidget(self.status_label)
        return box

    def _build_quick_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        base = QGroupBox("基础设置")
        form = QFormLayout(base)

        self.target_combo = QComboBox()
        self.target_combo.addItems(unity_locator.SUPPORTED_BUILD_TARGETS)
        form.addRow("目标平台：", self.target_combo)
        self.target_combo.currentTextChanged.connect(lambda v: self._set_state("buildTarget", v))

        self.pipeline_combo = QComboBox()
        self.pipeline_combo.addItems(unity_locator.BUILD_PIPELINES)
        form.addRow("默认构建管线：", self.pipeline_combo)
        self.pipeline_combo.currentTextChanged.connect(lambda v: self._set_state("buildPipeline", v))

        self.compress_combo = QComboBox()
        self.compress_combo.addItems(unity_locator.COMPRESS_OPTIONS)
        form.addRow("压缩方式：", self.compress_combo)
        self.compress_combo.currentTextChanged.connect(lambda v: self._set_state("compressOption", v))

        version_mode_row = QHBoxLayout()
        self.version_mode_combo = QComboBox()
        self.version_mode_combo.addItem("统一版本号 (所有包共用)", "Unified")
        self.version_mode_combo.addItem("独立版本号 (每包各自)", "PerPackage")
        self.version_mode_combo.currentIndexChanged.connect(self._on_version_mode_changed)
        version_mode_row.addWidget(self.version_mode_combo)
        version_mode_row.addStretch()
        form.addRow("版本号模式：", version_mode_row)

        unified_row = QHBoxLayout()
        self.version_edit = QLineEdit()
        unified_row.addWidget(self.version_edit, stretch=1)
        auto_btn = QPushButton("自动")
        auto_btn.setFixedWidth(56)
        auto_btn.clicked.connect(self._auto_version)
        unified_row.addWidget(auto_btn)
        self.unified_wrapper = QWidget()
        self.unified_wrapper.setLayout(unified_row)
        form.addRow("资源版本号：", self.unified_wrapper)
        self.version_edit.textChanged.connect(lambda v: self._set_state("packageVersion", v))

        per_pkg_box = QGroupBox("独立版本号（PerPackage）")
        per_layout = QVBoxLayout(per_pkg_box)
        self.per_package_table = QTableWidget(0, 2)
        self.per_package_table.setHorizontalHeaderLabels(["资源包", "版本号"])
        header = self.per_package_table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        self.per_package_table.verticalHeader().setVisible(False)
        per_layout.addWidget(self.per_package_table)
        per_buttons = QHBoxLayout()
        read_last = QPushButton("从上次构建读取")
        read_last.clicked.connect(self._refresh_package_versions)
        per_buttons.addWidget(read_last)
        all_auto = QPushButton("全部自动")
        all_auto.clicked.connect(self._auto_all_package_versions)
        per_buttons.addWidget(all_auto)
        per_buttons.addStretch()
        per_layout.addLayout(per_buttons)
        self.per_package_box = per_pkg_box
        form.addRow(self.per_package_box)

        out_row = QHBoxLayout()
        self.output_edit = QLineEdit()
        out_row.addWidget(self.output_edit, stretch=1)
        out_browse = QPushButton("浏览")
        out_browse.setFixedWidth(56)
        out_browse.clicked.connect(self._browse_output)
        out_row.addWidget(out_browse)
        out_open = QPushButton("打开")
        out_open.setFixedWidth(56)
        out_open.clicked.connect(lambda: unity_locator.open_in_file_manager(self._resolved_output_root()))
        out_row.addWidget(out_open)
        wrap = QWidget()
        wrap.setLayout(out_row)
        form.addRow("AB输出目录：", wrap)
        self.output_edit.textChanged.connect(lambda v: self._set_state("outputRoot", v))

        layout.addWidget(base)
        layout.addStretch()
        return page

    def _build_publish_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        publish = QGroupBox("发布整理")
        pub_form = QFormLayout(publish)
        self.publish_check = QCheckBox("启用发布整理")
        self.publish_check.toggled.connect(lambda v: self._set_state("enablePublishCopy", v))
        pub_form.addRow(self.publish_check)

        pub_dir_row = QHBoxLayout()
        self.publish_edit = QLineEdit()
        pub_dir_row.addWidget(self.publish_edit, stretch=1)
        pub_browse = QPushButton("浏览")
        pub_browse.setFixedWidth(56)
        pub_browse.clicked.connect(self._browse_publish)
        pub_dir_row.addWidget(pub_browse)
        pub_open = QPushButton("打开")
        pub_open.setFixedWidth(56)
        pub_open.clicked.connect(lambda: unity_locator.open_in_file_manager(self._resolved_publish_root()))
        pub_dir_row.addWidget(pub_open)
        pub_wrap = QWidget()
        pub_wrap.setLayout(pub_dir_row)
        pub_form.addRow("发布根目录：", pub_wrap)
        self.publish_edit.textChanged.connect(lambda v: self._set_state("publishRoot", v))

        self.clean_publish_check = QCheckBox("清空目标包目录后再拷贝")
        self.clean_publish_check.toggled.connect(lambda v: self._set_state("cleanPublishPackageDirectory", v))
        pub_form.addRow(self.clean_publish_check)
        layout.addWidget(publish)

        player = QGroupBox("Player")
        player_form = QFormLayout(player)
        self.build_player_check = QCheckBox("构建 Player（一键构建时生效）")
        self.build_player_check.toggled.connect(lambda v: self._set_state("buildPlayer", v))
        player_form.addRow(self.build_player_check)

        self.player_platform_combo = QComboBox()
        self.player_platform_combo.addItems(unity_locator.SUPPORTED_BUILD_TARGETS)
        self.player_platform_combo.currentTextChanged.connect(lambda v: self._set_state("playerPlatform", v))
        player_form.addRow("Player 平台：", self.player_platform_combo)

        player_out_row = QHBoxLayout()
        self.player_output_edit = QLineEdit()
        player_out_row.addWidget(self.player_output_edit, stretch=1)
        player_browse = QPushButton("浏览")
        player_browse.setFixedWidth(56)
        player_browse.clicked.connect(self._browse_player_output)
        player_out_row.addWidget(player_browse)
        player_wrap = QWidget()
        player_wrap.setLayout(player_out_row)
        player_form.addRow("输出路径：", player_wrap)
        self.player_output_edit.textChanged.connect(lambda v: self._set_state("playerOutputPath", v))
        layout.addWidget(player)

        layout.addStretch()
        return page

    def _build_advanced_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        hotfix = QGroupBox("热更 DLL")
        hf_form = QFormLayout(hotfix)
        self.hotfix_check = QCheckBox("构建前编译热更 DLL")
        self.hotfix_check.toggled.connect(lambda v: self._set_state("buildHotFixDll", v))
        hf_form.addRow(self.hotfix_check)
        layout.addWidget(hotfix)

        minimal = QGroupBox("最小包设置")
        min_form = QFormLayout(minimal)
        self.minimal_check = QCheckBox("启用最小包模式")
        self.minimal_check.toggled.connect(lambda v: self._set_state("minimalPackage", v))
        min_form.addRow(self.minimal_check)
        self.retain_edit = QLineEdit()
        self.retain_edit.setPlaceholderText("保留 Tag（逗号分隔），留空则仅保留清单")
        self.retain_edit.textChanged.connect(lambda v: self._set_state("retainTags", v))
        min_form.addRow("保留 Tag：", self.retain_edit)
        layout.addWidget(minimal)

        advanced = QGroupBox("高级设置")
        adv_form = QFormLayout(advanced)
        self.share_check = QCheckBox("启用共享资源打包")
        self.share_check.toggled.connect(lambda v: self._set_state("enableSharePackRule", v))
        adv_form.addRow(self.share_check)
        self.validate_check = QCheckBox("资源路径校验（Unicode 控制字符）")
        self.validate_check.toggled.connect(lambda v: self._set_state("enableAssetPathValidation", v))
        adv_form.addRow(self.validate_check)
        self.dep_db_check = QCheckBox("使用资源依赖数据库")
        self.dep_db_check.toggled.connect(lambda v: self._set_state("useAssetDependencyDB", v))
        adv_form.addRow(self.dep_db_check)
        self.clear_cache_check = QCheckBox("清理构建缓存")
        self.clear_cache_check.toggled.connect(lambda v: self._set_state("clearBuildCache", v))
        adv_form.addRow(self.clear_cache_check)
        self.verify_check = QCheckBox("验证构建结果")
        self.verify_check.toggled.connect(lambda v: self._set_state("verifyBuildingResult", v))
        adv_form.addRow(self.verify_check)

        self.copy_option_combo = QComboBox()
        self.copy_option_combo.addItems(unity_locator.BUNDLED_COPY_OPTIONS)
        self.copy_option_combo.currentTextChanged.connect(lambda v: self._set_state("buildinFileCopyOption", v))
        adv_form.addRow("内置文件拷贝：", self.copy_option_combo)

        self.filename_style_combo = QComboBox()
        self.filename_style_combo.addItems(unity_locator.FILE_NAME_STYLES)
        self.filename_style_combo.currentTextChanged.connect(lambda v: self._set_state("fileNameStyle", v))
        adv_form.addRow("文件名风格：", self.filename_style_combo)

        self.catalog_check = QCheckBox("在构建输出目录生成 Catalog")
        self.catalog_check.toggled.connect(lambda v: self._set_state("generateCatalogInOutput", v))
        adv_form.addRow(self.catalog_check)
        layout.addWidget(advanced)

        layout.addStretch()
        return page

    def _build_log_panel(self) -> QWidget:
        box = QGroupBox("构建日志")
        layout = QVBoxLayout(box)
        self.log_view = QPlainTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.setFont(QFont("Consolas", 9))
        self.log_view.setMaximumBlockCount(8000)
        layout.addWidget(self.log_view)

        buttons = QHBoxLayout()
        copy_btn = QPushButton("复制选中")
        copy_btn.clicked.connect(self._copy_log)
        buttons.addWidget(copy_btn)
        export_btn = QPushButton("导出日志")
        export_btn.clicked.connect(self._export_log)
        buttons.addWidget(export_btn)
        clear_btn = QPushButton("清空")
        clear_btn.clicked.connect(self.log_view.clear)
        buttons.addWidget(clear_btn)
        buttons.addStretch()
        layout.addLayout(buttons)
        return box

    # ============ 状态绑定 ============

    def _set_state(self, key: str, value) -> None:
        setattr(self.state, key, value)
        config_store.save_last_session(self.state)

    def _load_state_into_ui(self) -> None:
        s = self.state
        self.project_dir_edit.setText(s.projectDir)
        self.unity_exe_edit.setText(s.unityExePath)
        self.target_combo.setCurrentText(s.buildTarget)
        self.pipeline_combo.setCurrentText(s.buildPipeline)
        self.compress_combo.setCurrentText(s.compressOption)
        self.version_edit.setText(s.packageVersion)
        idx = self.version_mode_combo.findData(s.packageVersionMode)
        self.version_mode_combo.setCurrentIndex(max(idx, 0))
        self.output_edit.setText(s.outputRoot)
        self.publish_check.setChecked(s.enablePublishCopy)
        self.publish_edit.setText(s.publishRoot)
        self.clean_publish_check.setChecked(s.cleanPublishPackageDirectory)
        self.minimal_check.setChecked(s.minimalPackage)
        self.retain_edit.setText(s.retainTags)
        self.share_check.setChecked(s.enableSharePackRule)
        self.validate_check.setChecked(s.enableAssetPathValidation)
        self.dep_db_check.setChecked(s.useAssetDependencyDB)
        self.clear_cache_check.setChecked(s.clearBuildCache)
        self.verify_check.setChecked(s.verifyBuildingResult)
        self.copy_option_combo.setCurrentText(s.buildinFileCopyOption)
        self.filename_style_combo.setCurrentText(s.fileNameStyle)
        self.catalog_check.setChecked(s.generateCatalogInOutput)
        self.hotfix_check.setChecked(s.buildHotFixDll)
        self.build_player_check.setChecked(s.buildPlayer)
        self.player_platform_combo.setCurrentText(s.playerPlatform)
        self.player_output_edit.setText(s.playerOutputPath)

    # ============ 交互：版本号 ============

    def _on_version_mode_changed(self) -> None:
        mode = self.version_mode_combo.currentData()
        self._set_state("packageVersionMode", mode)
        is_unified = mode == "Unified"
        self.unified_wrapper.setVisible(is_unified)
        self.per_package_box.setVisible(not is_unified)
        if not is_unified and self.per_package_table.rowCount() == 0:
            self._refresh_package_versions()

    def _auto_version(self) -> None:
        self.version_edit.setText(config_store.default_package_version())

    def _refresh_package_versions(self, silent: bool = False) -> None:
        project_dir = self._current_project_dir()
        packages = config_store.load_runtime_package_names(project_dir)
        last = config_store.read_last_package_versions(self.state.buildTarget, self.state.outputRoot, project_dir)
        existing = {e.packageName: e.version for e in self.state.packageVersions}

        table = self.per_package_table
        table.setRowCount(len(packages))
        self.state.packageVersions = []
        for row, name in enumerate(packages):
            version = existing.get(name) or last.get(name, "")
            table.setItem(row, 0, QTableWidgetItem(name))
            item = QTableWidgetItem(version)
            table.setItem(row, 1, item)
            self.state.packageVersions.append(PackageVersionEntry(name, version))
        if not silent and not any(e.version for e in self.state.packageVersions):
            self._append_log("[BuildCLI] 未发现历史构建版本，可点「全部自动」生成。")

    def _on_package_version_changed(self, item: QTableWidgetItem) -> None:
        if item.column() != 1:
            return
        row = item.row()
        name_item = self.per_package_table.item(row, 0)
        if name_item is None:
            return
        name = name_item.text()
        for entry in self.state.packageVersions:
            if entry.packageName == name:
                entry.version = item.text()
                break
        config_store.save_last_session(self.state)

    def _auto_all_package_versions(self) -> None:
        version = config_store.default_package_version()
        for row in range(self.per_package_table.rowCount()):
            self.per_package_table.item(row, 1).setText(version)

    # ============ 交互：路径 ============

    def _current_project_dir(self) -> Path:
        text = self.state.projectDir.strip() if self.state.projectDir else ""
        return Path(text) if text else unity_locator.PROJECT_DIR

    def _resolved_output_root(self) -> Path:
        root = self.state.outputRoot or "./Releases/Bundles/"
        path = Path(root)
        return path if path.is_absolute() else self._current_project_dir() / str(path).lstrip("./")

    def _resolved_publish_root(self) -> Path:
        root = self.state.publishRoot or "./Releases/Publish/"
        path = Path(root)
        return path if path.is_absolute() else self._current_project_dir() / str(path).lstrip("./")

    def _browse_project_dir(self) -> None:
        current = self.project_dir_edit.text().strip() or str(unity_locator.PROJECT_DIR)
        path = QFileDialog.getExistingDirectory(self, "选择 Unity 工程目录（含 Assets/ProjectSettings）", current)
        if path:
            self.project_dir_edit.setText(path)

    def _on_project_dir_changed(self, text: str) -> None:
        """项目目录联动：写入状态 + 校验合法性 + 刷新 Unity 版本显示 + 重定位匹配版本的 Unity.exe。"""
        text = text.strip()
        self._set_state("projectDir", text)
        if not text:
            self.project_dir_valid_label.setText("")
            self.unity_version_label.setText("")
            return

        path = Path(text)
        is_valid = (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()
        self.project_dir_valid_label.setText("✓ 有效工程目录" if is_valid else "✗ 缺少 Assets/ProjectSettings")
        self.project_dir_valid_label.setStyleSheet("color: green;" if is_valid else "color: #d33;")

        version = unity_locator.read_project_unity_version(project_dir=path) if is_valid else None
        self.unity_version_label.setText(f"项目版本：{version or '未知'}")

        # 版本变化时自动重定位匹配的 Unity.exe（仅当当前 Unity 路径与该版本不匹配时）
        if version and not self.unity_exe_edit.text().strip():
            exe = unity_locator.locate_unity_exe(preferred_version=version)
            if exe:
                self.unity_exe_edit.setText(str(exe))
                self._set_state("unityExePath", str(exe))

    def _browse_unity(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "选择 Unity.exe", "", "Unity (Unity.exe)")
        if path:
            self.unity_exe_edit.setText(path)
            self._set_state("unityExePath", path)

    def _auto_locate_unity(self) -> None:
        project_dir = Path(self.project_dir_edit.text().strip()) if self.project_dir_edit.text().strip() else None
        exe = unity_locator.locate_unity_exe(
            preferred_version=unity_locator.read_project_unity_version(project_dir=project_dir),
            custom_path=self.unity_exe_edit.text().strip() or None,
        )
        if exe:
            self.unity_exe_edit.setText(str(exe))
            self._set_state("unityExePath", str(exe))
        else:
            QMessageBox.warning(self, "未找到 Unity", "未在 Unity Hub / Program Files 下找到 Unity，请手动浏览指定。")

    def _browse_output(self) -> None:
        path = QFileDialog.getExistingDirectory(self, "选择 AB 输出根目录", str(self._resolved_output_root()))
        if path:
            self.output_edit.setText(path)

    def _browse_publish(self) -> None:
        path = QFileDialog.getExistingDirectory(self, "选择发布根目录", str(self._resolved_publish_root()))
        if path:
            self.publish_edit.setText(path)

    def _browse_player_output(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Player 输出路径", self.state.playerOutputPath)
        if path:
            self.player_output_edit.setText(path)

    # ============ 交互：预设 ============

    def _reload_preset_combo(self) -> None:
        self.preset_combo.clear()
        for p in config_store.list_presets():
            self.preset_combo.addItem(p.stem)

    def _save_preset(self) -> None:
        name = self.preset_combo.currentText().strip()
        from PySide6.QtWidgets import QInputDialog
        name, ok = QInputDialog.getText(self, "保存预设", "预设名：", text=name)
        if not ok or not name.strip():
            return
        path = config_store.save_preset(name.strip(), self.state)
        self._append_log(f"[BuildCLI] 预设已保存：{path}")
        self._reload_preset_combo()
        self.preset_combo.setCurrentText(name.strip())

    def _load_preset(self) -> None:
        name = self.preset_combo.currentText().strip()
        if not name:
            return
        path = config_store.PRESETS_DIR / f"{name}.json"
        if not path.exists():
            QMessageBox.warning(self, "预设不存在", f"未找到预设：{path}")
            return
        unity_exe = self.state.unityExePath
        project_dir = self.state.projectDir
        self.state = config_store.load_preset(path)
        # 环境字段以当前会话为准（预设可能来自其他机器/项目）
        if not self.state.unityExePath:
            self.state.unityExePath = unity_exe
        if not self.state.projectDir or not Path(self.state.projectDir).is_dir():
            self.state.projectDir = project_dir
        self._load_state_into_ui()
        self._on_version_mode_changed()
        self._append_log(f"[BuildCLI] 已加载预设：{name}")

    def _load_from_asset(self) -> None:
        project_dir = self._current_project_dir()
        loaded = config_store.load_form_from_build_pipeline_setting(project_dir)
        if loaded is None:
            QMessageBox.warning(self, "读取失败", f"未能解析：{config_store.build_pipeline_setting_asset(project_dir)}")
            return
        unity_exe = self.state.unityExePath
        loaded.unityExePath = unity_exe
        loaded.projectDir = str(project_dir)
        self.state = loaded
        self._load_state_into_ui()
        self._on_version_mode_changed()
        self._append_log("[BuildCLI] 已从 Unity 打包窗口配置（BuildPipelineSetting.asset）读取。")

    # ============ 构建执行 ============

    def _start_action(self, action: str) -> None:
        if self.run is not None and self.run.result is None:
            QMessageBox.information(self, "构建中", "已有构建正在进行，请先取消或等待完成。")
            return

        unity_exe = self.unity_exe_edit.text().strip()
        if not unity_exe or not Path(unity_exe).is_file():
            QMessageBox.warning(self, "缺少 Unity 路径", "请先设置有效的 Unity.exe 路径。")
            return

        project_dir = self.project_dir_edit.text().strip()
        if not project_dir or not (Path(project_dir) / "Assets").is_dir():
            QMessageBox.warning(self, "项目目录无效", "请设置有效的 Unity 工程目录（含 Assets/ProjectSettings）。")
            return

        self.state.action = action
        self.state.unityExePath = unity_exe
        self.state.projectDir = project_dir
        config_store.save_last_session(self.state)

        action_names = {
            "build": "一键构建 (AB + Player)", "buildAb": "构建 AssetBundle", "buildPlayer": "构建 Player",
            "publish": "发布整理", "hotfixDll": "编译并拷贝热更DLL", "generateAll": "GenerateAll",
            "syncAotManifest": "同步 AOT 元数据清单", "copyAotDll": "拷贝 AOT 元数据 DLL",
            "switchPlatform": "切换目标平台",
        }
        if action in ("build", "buildAb", "publish") and self.state.packageVersionMode == "Unified" and not self.state.packageVersion:
            self.state.packageVersion = config_store.default_package_version()
            self.version_edit.setText(self.state.packageVersion)
            self._append_log(f"[BuildCLI] 版本号为空，自动生成：{self.state.packageVersion}")

        if action == "build" and self.state.buildPlayer and self.state.playerPlatform != self.state.buildTarget:
            QMessageBox.warning(self, "平台不一致", f"Player 平台 {self.state.playerPlatform} 与资源包平台 {self.state.buildTarget} 不一致，Unity 侧会拒绝构建。")
            return

        self.log_view.clear()
        self._append_log(f"[BuildCLI] ====== {action_names.get(action, action)} ======")
        self.run = BuildRun(self.state, Path(unity_exe))
        self.run.on_log(self._append_log)
        self.run.on_done(self._on_build_done)
        self._set_building_ui(True)
        if self.run.start():
            self.pump_timer.start()
        else:
            self._set_building_ui(False)

    def _cancel_build(self) -> None:
        if self.run:
            self.run.cancel()
            self.pump_timer.stop()
            self._set_building_ui(False)

    def _pump(self) -> None:
        if self.run and not self.run.pump():
            self.pump_timer.stop()

    def _on_build_done(self, result: str) -> None:
        self._set_building_ui(False)
        if result == "success":
            self.status_label.setText("构建成功")
            self._append_log("[BuildCLI] ====== 构建成功 ======")
        elif result == "cancelled":
            self.status_label.setText("已取消")
            self._append_log("[BuildCLI] ====== 已取消 ======")
        else:
            self.status_label.setText("构建失败")
            self._append_log("[BuildCLI] ====== 构建失败，详见日志 ======")
            QMessageBox.warning(self, "构建失败", f"构建失败，完整日志：\n{self.run.log_file if self.run else '?'}")

    def _set_building_ui(self, building: bool) -> None:
        self.cancel_button.setEnabled(building)
        self.status_label.setText("构建中…" if building else self.status_label.text())
        for bar_btn in self.findChildren(QPushButton):
            if bar_btn is not self.cancel_button:
                bar_btn.setEnabled(not building)

    # ============ 日志 ============

    def _append_log(self, line: str) -> None:
        self.log_view.appendPlainText(line)
        self.log_view.moveCursor(QTextCursor.MoveOperation.End)

    def _copy_log(self) -> None:
        selected = self.log_view.textCursor().selectedText()
        if selected:
            QApplication.clipboard().setText(selected.replace("\u2029", "\n"))

    def _export_log(self) -> None:
        if self.run is None:
            return
        path, _ = QFileDialog.getSaveFileName(self, "导出日志", str(self.run.log_file))
        if path:
            Path(path).write_text(self.log_view.toPlainText(), encoding="utf-8")

    def closeEvent(self, event) -> None:
        if self.run and self.run.result is None:
            answer = QMessageBox.question(self, "构建进行中", "Unity 构建仍在进行，退出会取消构建。确定退出？")
            if answer != QMessageBox.StandardButton.Yes:
                event.ignore()
                return
            self.run.cancel()
        config_store.save_last_session(self.state)
        super().closeEvent(event)


def run_app() -> int:
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    return app.exec()
