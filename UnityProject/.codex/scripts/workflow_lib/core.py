"""Process execution, evidence, paths, and one-attempt approvals."""

from __future__ import annotations

import contextlib
import hashlib
import json
import locale
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile as tempfile_module
import time
import uuid
from datetime import datetime, timezone


class WorkflowError(Exception):
    def __init__(self, message, status="failed"):
        super().__init__(message)
        self.status = status


def blocked(message):
    raise WorkflowError(message, "blocked")


def utcnow():
    return datetime.now(timezone.utc).isoformat()


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":"))


def digest(value):
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()


def file_hash(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise WorkflowError(f"Cannot read JSON {path}: {exc}") from exc


def write_json(path, value, attempts=6, delay=0.05):
    """Atomically replace a JSON file, tolerating transient Windows sharing violations.

    Antivirus and the search indexer briefly open freshly written files, which makes os.replace
    fail with PermissionError even though nothing is wrong with the content. Retrying a few times
    keeps a passing step from being reported as an unexpected failure; a persistent error still
    propagates. The temporary file is removed on failure so a stale .tmp cannot be mistaken for
    evidence.
    """
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for attempt in range(attempts):
        try:
            os.replace(temporary, path)
            return
        except PermissionError:
            if attempt == attempts - 1:
                temporary.unlink(missing_ok=True)
                raise
            time.sleep(delay * (attempt + 1))


SECRET_KEY = re.compile(r"(token|password|authorization|secret|api.?key|cookie)", re.I)
SECRET_TEXT = re.compile(
    r'(?i)(bearer\s+|basic\s+)[A-Za-z0-9._~+/=-]+|'
    r'(["\']?(?:evalToken|access_token|api_key|password|authorization|secret)["\']?\s*[:=]\s*)'
    r'(?:(?:bearer|basic)\s+)?("[^"]*"|\'[^\']*\'|[^\s,}]+)'
)


def redact(value):
    if isinstance(value, dict):
        return {k: "[REDACTED]" if SECRET_KEY.search(k) else redact(v) for k, v in value.items()}
    if isinstance(value, list):
        return [redact(item) for item in value]
    if isinstance(value, str):
        if value.lstrip().startswith(("{", "[")):
            try:
                parsed = json.loads(value)
            except ValueError:
                pass
            else:
                return json.dumps(redact(parsed), ensure_ascii=False)
        return SECRET_TEXT.sub(lambda m: (m.group(1) or m.group(2)) + "[REDACTED]", value)
    return value


def within(path, root):
    path, root = Path(path).resolve(), Path(root).resolve()
    if not path.is_relative_to(root):
        raise WorkflowError(f"Path escapes allowed root: {path}")
    return path


def unity_package(context, name):
    """Resolve one package's installed version and directory across embedded, local, and registry layouts.

    Registry packages live under Library/PackageCache/<name>@<hash>, not Packages/<name>, so the
    manifest declaration alone cannot be read as a path.
    """
    manifest = (read_json(context.project / "Packages" / "manifest.json").get("dependencies") or {})
    lock_path = context.project / "Packages" / "packages-lock.json"
    lock = read_json(lock_path).get("dependencies", {}).get(name, {}) if lock_path.is_file() else {}
    declared = manifest.get(name)
    if not lock and not declared:
        blocked(f"Package {name} is not declared in Packages/manifest.json and has no packages-lock.json entry.")
    directories = []
    embedded = context.project / "Packages" / name
    if embedded.is_dir():
        directories.append(embedded)
    if isinstance(declared, str) and declared.startswith("file:"):
        local = (context.project / "Packages" / declared[len("file:"):].replace("\\", "/")).resolve()
        if local.is_dir():
            directories.append(local)
    cache = context.project / "Library" / "PackageCache"
    if cache.is_dir():
        directories.extend(sorted(path for path in cache.glob(name + "@*") if path.is_dir()))
    installed = [directory / "package.json" for directory in directories if (directory / "package.json").is_file()]
    expected = lock.get("version")
    matched = [path for path in installed if expected and read_json(path).get("version") == expected]
    chosen = (matched or installed or [None])[0]
    version = read_json(chosen).get("version") if chosen else expected
    if not version:
        blocked(f"Cannot determine the installed version of {name}; open the project in Unity so packages resolve.")
    return {"name": name, "version": version,
            "source": lock.get("source") or ("embedded" if chosen and chosen.parent == embedded else "declared"),
            "expected": expected, "path": str(chosen.parent) if chosen else None}


def locate_cli_for(project):
    """Prefer the pinned project-local CLI, then an explicitly installed `unity` CLI on PATH.

    The Editor's own Unity.exe is never a substitute; it lacks the `command`/`status` subcommands
    this workflow drives.
    """
    local = Path(project) / "Tools" / "unity.exe"
    if local.is_file():
        return local, "project"
    found = shutil.which("unity")
    if found:
        resolved = Path(found).resolve()
        if resolved.parent.name.lower() != "editor":
            return resolved, "path"
    return local, "missing"


class Context:
    def __init__(self, project=None):
        self.project = Path(project or Path(__file__).resolve().parents[3]).resolve()
        for folder in ("Assets", "Packages", "ProjectSettings"):
            if not (self.project / folder).is_dir():
                blocked(f"Not a Unity project: missing {self.project / folder}")
        self.repo = self.project.parent
        self.scripts = self.project / ".codex" / "scripts"
        self.runs = self.project / ".codex" / "runs"
        self.cli, self.cliSource = self.locate_cli()
        self.solution = self.project / "UnityProject.sln"

    def locate_cli(self):
        return locate_cli_for(self.project)


def temp_environment(project):
    """Return a TMP/TEMP override only when the inherited temp directory is unusable.

    Some sandboxes deny child processes writes to the system temp directory. Build tools such as
    MSBuild create a temp subdirectory at startup and fail hard when that is denied, so fall back
    to a project-local directory instead of silently degrading the build step.
    """
    for key in ("TMP", "TEMP", "TMPDIR"):
        candidate = os.environ.get(key)
        if not candidate:
            continue
        try:
            probe = tempfile_module.mkdtemp(prefix="workflow-probe-", dir=candidate)
        except OSError:
            continue
        try:
            os.rmdir(probe)
        except OSError:
            pass
        return {}
    fallback = Path(project) / ".codex" / "tmp"
    fallback.mkdir(parents=True, exist_ok=True)
    return {"TMP": str(fallback), "TEMP": str(fallback), "TMPDIR": str(fallback)}


class Run:
    def __init__(self, context, command):
        self.context = context
        self.temp_env = None
        self.path = context.runs / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
        self.path.mkdir(parents=True)
        self.data = {
            "schemaVersion": 1, "runId": self.path.name, "project": str(context.project),
            "command": command, "startedAt": utcnow(), "finishedAt": None,
            "status": "blocked", "steps": [], "commands": [],
        }
        self.save()

    def save(self):
        write_json(self.path / "report.json", redact(self.data))

    def step(self, name, function, required=True):
        entry = {"name": name, "required": required, "status": "blocked", "startedAt": utcnow()}
        self.data["steps"].append(entry)
        self.save()
        try:
            result = function()
            entry.update(status="passed", result=redact(result))
        except WorkflowError as exc:
            entry.update(status=exc.status, message=str(exc))
        except Exception as exc:
            entry.update(status="failed", message=f"{type(exc).__name__}: {exc}")
        entry["finishedAt"] = utcnow()
        self.save()
        return entry

    def skip(self, name, reason, required=False):
        self.data["steps"].append({"name": name, "required": required, "status": "skipped", "message": reason})
        self.save()

    def execute(self, args, cwd=None, timeout=60, env=None, mutating=False):
        args = [str(arg) for arg in args]
        cwd = Path(cwd or self.context.project)
        record = {"args": args, "cwd": str(cwd), "startedAt": utcnow(), "mutating": mutating}
        self.data["commands"].append(record)
        self.save()
        try:
            child_env = dict(os.environ if env is None else env)
            child_env["PYTHONUTF8"] = "1"
            if self.temp_env is None:
                self.temp_env = temp_environment(self.context.project)
            if self.temp_env:
                child_env.update(self.temp_env)
                record["tempDir"] = self.temp_env["TMP"]
            completed = subprocess.run(
                args, cwd=cwd, env=child_env, capture_output=True, timeout=timeout, shell=False,
            )
            completed.stdout = decode_output(completed.stdout)
            completed.stderr = decode_output(completed.stderr)
            record.update(exitCode=completed.returncode, stdout=redact(completed.stdout), stderr=redact(completed.stderr))
        except FileNotFoundError as exc:
            record.update(error="executable_not_found")
            blocked(f"Executable not found: {args[0]}")
        except subprocess.TimeoutExpired as exc:
            record.update(error="timeout", stdout=redact(decode_output(exc.stdout or b"")),
                          stderr=redact(decode_output(exc.stderr or b"")))
            suffix = " Mutation outcome is unknown; inspect state, do not replay." if mutating else ""
            blocked(f"Command timed out after {timeout}s.{suffix}")
        finally:
            record["finishedAt"] = utcnow()
            self.save()
        return completed

    def finish(self):
        self.data["finishedAt"] = utcnow()
        self.data["status"] = aggregate(self.data["steps"])
        self.save()
        render_report(self.path, self.data)
        return {"passed": 0, "failed": 1, "blocked": 2}[self.data["status"]]


def aggregate(steps):
    if any(step["status"] == "failed" for step in steps):
        return "failed"
    required = [step for step in steps if step.get("required", True)]
    if not required or any(step["status"] != "passed" for step in required):
        return "blocked"
    return "passed"


def decode_output(value):
    if isinstance(value, str):
        return value
    try:
        return value.decode("utf-8-sig")
    except UnicodeDecodeError:
        return value.decode(locale.getencoding(), errors="replace")


def render_report(path, data=None):
    path = Path(path)
    data = data or read_json(path / "report.json")
    # An interrupted run stays blocked even if its completed steps passed.
    status = aggregate(data["steps"]) if data.get("finishedAt") else "blocked"
    lines = [f"# Workflow {data['runId']}", "", f"- Status: {status}",
             f"- Project: `{data['project']}`", f"- Command: `{data['command']}`", ""]
    for step in data["steps"]:
        lines.append(f"## {step['name']}: {step['status']}")
        lines.append(step.get("message", json.dumps(step.get("result", ""), ensure_ascii=False)))
        lines.append("")
    lines.extend(["## Evidence", "", "See report.json for redacted commands, output, timestamps, and exit codes.", ""])
    (path / "report.md").write_text("\n".join(lines), encoding="utf-8")
    return status


def require_success(completed, description):
    if completed.returncode:
        raise WorkflowError(f"{description} exited with {completed.returncode}; see command evidence.")
    return completed.stdout


@contextlib.contextmanager
def project_lock(context):
    context.runs.mkdir(parents=True, exist_ok=True)
    path = context.runs / "editor.lock"
    try:
        fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        blocked(f"Project operation already locked: {path}. Inspect the owner; do not delete an active lock.")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            stream.write(canonical({"pid": os.getpid(), "createdAt": utcnow(), "project": str(context.project)}))
        yield
    finally:
        path.unlink(missing_ok=True)


def workspace_fingerprint(context):
    if not shutil.which("git"):
        blocked("Git is required to bind approval to the current worktree.")
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=context.repo, capture_output=True)
    files = subprocess.run(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
        cwd=context.repo, capture_output=True,
    )
    if head.returncode or files.returncode:
        blocked("Cannot fingerprint repository; approval requires an initialized Git worktree.")
    hashes = {}
    for item in sorted(set(files.stdout.split(b"\0"))):
        if not item:
            continue
        name = os.fsdecode(item)
        path = context.repo / name
        # Git-tracked files plus non-ignored inputs; never follow symlinks out of the repository.
        if path.is_symlink():
            hashes[name] = "link:" + os.readlink(path)
        elif path.is_file():
            hashes[name] = file_hash(path)
        else:
            hashes[name] = "missing"
    return digest({"head": head.stdout.decode().strip(), "files": hashes})


def make_plan(run, kind, command, parameters, high_risk=False, inputs=(), preview=None):
    context = run.context
    bound_inputs = {}
    for filename in inputs:
        path = Path(filename).resolve()
        if not path.is_file():
            blocked(f"Approval input is missing: {path}")
        bound_inputs[str(path)] = file_hash(path)
    plan = {
        "schemaVersion": 1, "kind": kind, "command": command, "parameters": parameters,
        "project": str(context.project), "risk": "high" if high_risk else "normal",
        "createdAt": utcnow(), "expiresAt": time.time() + 3600,
        "worktree": workspace_fingerprint(context), "inputs": bound_inputs,
        "preview": preview, "nonce": uuid.uuid4().hex,
    }
    path = run.path / "preview.json"
    write_json(path, plan)
    return {"plan": str(path), "planHash": digest(plan), "risk": plan["risk"], "preview": preview}


def validate_plan(context, plan):
    if plan.get("schemaVersion") != 1:
        blocked("Unsupported plan schema.")
    if Path(plan.get("project", "")).resolve() != context.project:
        blocked("Approval belongs to a different project.")
    if time.time() >= plan.get("expiresAt", 0):
        blocked("Preview expired; create a new preview.")
    if workspace_fingerprint(context) != plan.get("worktree"):
        blocked("Worktree changed since preview; preview and authorize again.")
    for filename, expected in plan.get("inputs", {}).items():
        if not Path(filename).is_file() or file_hash(filename) != expected:
            blocked(f"Input changed since preview: {filename}")


def approve(run, plan_file, by, reason, high_risk=False):
    plan = read_json(plan_file)
    validate_plan(run.context, plan)
    if not by.strip() or not reason.strip():
        blocked("Approval requires an approver and the actual confirmation reference.")
    if plan["risk"] == "high" and not high_risk:
        blocked("This operation requires separate high-risk confirmation (--high-risk).")
    approval = {
        "schemaVersion": 1, "project": str(run.context.project), "planHash": digest(plan),
        "approvedBy": by, "reason": reason, "approvedAt": utcnow(),
        "expiresAt": plan["expiresAt"], "approvalId": uuid.uuid4().hex,
        "highRisk": high_risk,
    }
    path = run.path / "approval.json"
    write_json(path, approval)
    return {"approval": str(path), "planHash": approval["planHash"]}


def consume_approval(run, plan, approval_file):
    approval = read_json(approval_file)
    validate_plan(run.context, plan)
    if approval.get("schemaVersion") != 1 or approval.get("project") != str(run.context.project):
        blocked("Invalid approval schema or project.")
    if approval.get("planHash") != digest(plan) or time.time() >= approval.get("expiresAt", 0):
        blocked("Approval does not match this preview or has expired.")
    if plan["risk"] == "high" and not approval.get("highRisk"):
        blocked("Missing high-risk approval.")
    # Bind consumption to the preview, including when it is approved twice.
    receipt = run.context.runs / "approvals" / (digest(plan) + ".used")
    receipt.parent.mkdir(parents=True, exist_ok=True)
    try:
        fd = os.open(receipt, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        blocked("Approval already consumed. Inspect prior outcome before requesting another attempt.")
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        stream.write(canonical({"runId": run.path.name, "consumedAt": utcnow()}))
    return approval
