"""Deterministic tests for process, evidence, and project-context helpers."""

import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))

from workflow_lib.core import (  # noqa: E402
    Context, WorkflowError, aggregate, locate_cli_for, redact, temp_environment, unity_package,
    write_json,
)


def make_project(root):
    project = Path(root) / "UnityProject"
    for folder in ("Assets", "Packages", "ProjectSettings"):
        (project / folder).mkdir(parents=True)
    return project


def write_package_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


class UnityPackageTest(unittest.TestCase):
    def test_registry_package_resolves_from_package_cache(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            write_package_json(project / "Packages" / "manifest.json",
                       {"dependencies": {"com.unity.pipeline": "0.8.0-exp.1"}})
            write_package_json(project / "Packages" / "packages-lock.json",
                       {"dependencies": {"com.unity.pipeline": {"version": "0.8.0-exp.1", "source": "registry"}}})
            cache = project / "Library" / "PackageCache" / "com.unity.pipeline@62c08c808737"
            write_package_json(cache / "package.json", {"name": "com.unity.pipeline", "version": "0.8.0-exp.1"})
            resolved = unity_package(Context(project), "com.unity.pipeline")
            self.assertEqual(resolved["version"], "0.8.0-exp.1")
            self.assertEqual(resolved["source"], "registry")
            self.assertEqual(Path(resolved["path"]).resolve(), cache.resolve())

    def test_embedded_package_wins_over_stale_cache(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            write_package_json(project / "Packages" / "manifest.json",
                       {"dependencies": {"com.tuyoogame.yooasset": "file:com.tuyoogame.yooasset"}})
            write_package_json(project / "Packages" / "packages-lock.json",
                       {"dependencies": {"com.tuyoogame.yooasset": {"version": "file:com.tuyoogame.yooasset",
                                                                    "source": "embedded"}}})
            embedded = project / "Packages" / "com.tuyoogame.yooasset"
            write_package_json(embedded / "package.json", {"version": "3.0.6"})
            write_package_json(project / "Library" / "PackageCache" / "com.tuyoogame.yooasset@deadbeef" / "package.json",
                       {"version": "2.3.0"})
            resolved = unity_package(Context(project), "com.tuyoogame.yooasset")
            self.assertEqual(resolved["version"], "3.0.6")
            self.assertEqual(Path(resolved["path"]).resolve(), embedded.resolve())

    def test_undeclared_package_blocks_instead_of_guessing(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            write_package_json(project / "Packages" / "manifest.json", {"dependencies": {}})
            with self.assertRaises(WorkflowError) as error:
                unity_package(Context(project), "com.example.absent")
            self.assertEqual(error.exception.status, "blocked")

    def test_declared_but_unresolved_package_reports_missing_version(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            write_package_json(project / "Packages" / "manifest.json", {"dependencies": {"com.example.gitpkg": "https://x"}})
            with self.assertRaises(WorkflowError) as error:
                unity_package(Context(project), "com.example.gitpkg")
            self.assertEqual(error.exception.status, "blocked")
            self.assertIn("open the project in Unity", str(error.exception))


class LocateCliTest(unittest.TestCase):
    def test_project_tools_binary_takes_precedence(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            local = project / "Tools" / "unity.exe"
            local.parent.mkdir()
            local.write_bytes(b"")
            with mock.patch("shutil.which", return_value="C:/elsewhere/unity.exe"):
                self.assertEqual(locate_cli_for(project), (local, "project"))

    def test_path_cli_is_accepted_when_project_tools_is_absent(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            with mock.patch("shutil.which", return_value="C:/Users/x/AppData/Local/Unity/bin/unity.exe"):
                resolved, source = locate_cli_for(project)
            self.assertEqual(source, "path")
            self.assertEqual(resolved.name, "unity.exe")

    def test_editor_bundled_executable_is_never_used(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            with mock.patch("shutil.which", return_value="C:/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"):
                _, source = locate_cli_for(project)
            self.assertEqual(source, "missing")


class TempEnvironmentTest(unittest.TestCase):
    def test_writable_system_temp_is_left_alone(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            with mock.patch.dict("os.environ", {"TMP": root, "TEMP": root}, clear=False):
                self.assertEqual(temp_environment(project), {})

    def test_unusable_system_temp_falls_back_inside_the_project(self):
        with tempfile.TemporaryDirectory() as root:
            project = make_project(root)
            with mock.patch.dict("os.environ", {"TMP": root, "TEMP": root}, clear=False), \
                    mock.patch("workflow_lib.core.tempfile_module.mkdtemp", side_effect=PermissionError("denied")):
                result = temp_environment(project)
            self.assertEqual(Path(result["TMP"]), project / ".codex" / "tmp")
            self.assertTrue((project / ".codex" / "tmp").is_dir())


class WriteJsonTest(unittest.TestCase):
    def test_transient_sharing_violation_is_retried(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "report.json"
            calls = {"n": 0}

            def flaky_replace(source, destination):
                calls["n"] += 1
                if calls["n"] < 3:
                    raise PermissionError(13, "sharing violation", str(destination))
                return real_replace(source, destination)

            real_replace = os.replace
            with mock.patch("workflow_lib.core.os.replace", side_effect=flaky_replace):
                write_json(target, {"status": "passed"})
            self.assertEqual(calls["n"], 3)
            self.assertEqual(json.loads(target.read_text(encoding="utf-8")), {"status": "passed"})
            self.assertFalse(target.with_suffix(".json.tmp").exists())

    def test_persistent_violation_still_propagates_and_cleans_up(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "report.json"
            with mock.patch("workflow_lib.core.os.replace",
                            side_effect=PermissionError(13, "denied")):
                with self.assertRaises(PermissionError):
                    write_json(target, {"status": "passed"}, attempts=2, delay=0)
            self.assertFalse(target.with_suffix(".json.tmp").exists())

    def test_exhausted_retries_never_leave_a_partial_report(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "report.json"
            target.write_text('{"status": "old"}', encoding="utf-8")
            with mock.patch("workflow_lib.core.os.replace", side_effect=PermissionError(13, "denied")):
                with self.assertRaises(PermissionError):
                    write_json(target, {"status": "new"}, attempts=2, delay=0)
            self.assertEqual(json.loads(target.read_text(encoding="utf-8")), {"status": "old"})


class AggregateTest(unittest.TestCase):
    def test_required_failure_dominates_blocked(self):
        self.assertEqual(aggregate([{"status": "blocked"}, {"status": "failed"}]), "failed")
        self.assertEqual(aggregate([{"status": "passed"}, {"status": "blocked"}]), "blocked")
        self.assertEqual(aggregate([{"status": "passed"}]), "passed")

    def test_non_required_step_does_not_block(self):
        self.assertEqual(aggregate([{"status": "passed"}, {"status": "skipped", "required": False}]), "passed")

    def test_empty_step_list_is_not_a_pass(self):
        self.assertEqual(aggregate([]), "blocked")


class RedactTest(unittest.TestCase):
    def test_secret_keys_and_bearer_tokens_are_masked(self):
        value = {"evalToken": "abc", "nested": [{"password": "p"}], "text": "authorization: Bearer abc.def"}
        result = redact(value)
        self.assertEqual(result["evalToken"], "[REDACTED]")
        self.assertEqual(result["nested"][0]["password"], "[REDACTED]")
        self.assertIn("[REDACTED]", result["text"])
        self.assertNotIn("abc.def", result["text"])

    def test_json_encoded_secrets_are_masked(self):
        result = json.loads(redact(json.dumps({"api_key": "secret-value"})))
        self.assertEqual(result["api_key"], "[REDACTED]")


if __name__ == "__main__":
    unittest.main()
