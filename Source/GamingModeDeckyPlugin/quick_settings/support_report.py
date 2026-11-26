"""Invoke Playhub's shared diagnostic collector without opening its UI."""
import json
import os
from pathlib import Path
import subprocess
import threading
import uuid

_gate = threading.Lock()


# Rebrand phase 2: Core.exe in Programs\Core for new installs; an existing install keeps its
# folder (Programs\Playhub) and may still hold Playhub.exe until it is updated.
_APP_EXECUTABLES = ("Core.exe", "Playhub.exe")


def _install_candidates(local):
    candidates = []
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub") as key:
            location, _ = winreg.QueryValueEx(key, "InstallLocation")
            if isinstance(location, str) and Path(location).is_absolute():
                candidates.append(Path(location))
    except (OSError, ImportError):
        pass
    programs = Path(local) / "Programs"
    candidates += [programs / "Core", programs / "Playhub"]
    return candidates


def _installed_app(local=None):
    local = os.environ.get("LOCALAPPDATA", "") if local is None else local
    candidates = _install_candidates(local)
    for folder in candidates:
        for name in _APP_EXECUTABLES:
            if (folder / name).is_file():
                return folder / name
    return candidates[0] / _APP_EXECUTABLES[0]


def local_data_root(local=None):
    """%LOCALAPPDATA%\\Core once the app has copied its data there (marker file), else Playhub."""
    local = Path(os.environ.get("LOCALAPPDATA", "") if local is None else local)
    current, legacy = local / "Core", local / "Playhub"
    return current if (current / ".core-migrated.json").is_file() or not legacy.is_dir() else legacy


def generate(*, app=None, local_root=None, runner=subprocess.run):
    if not _gate.acquire(blocking=False):
        return {"ok": False, "message": "A diagnostic report is already being generated."}
    receipt = None
    try:
        app = Path(app) if app is not None else _installed_app()
        marker = app.parent / "Assets/support-report-v1.json"
        if not app.is_file() or not marker.is_file():
            return {"ok": False, "message": "Update the Core app to generate its complete diagnostic report."}
        protocol = json.loads(marker.read_text(encoding="utf-8-sig"))
        if not isinstance(protocol, dict) or protocol.get("protocol") != 1:
            return {"ok": False, "message": "Unsupported Core diagnostic protocol."}
        root = Path(local_root) if local_root is not None else local_data_root()
        request = uuid.uuid4().hex
        receipt = root / "DiagnosticsRequests" / (request + ".json")
        result = runner([str(app), "diagnostics-report", request], cwd=str(app.parent),
                        timeout=180, capture_output=True,
                        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        if result.returncode != 0 or not receipt.is_file():
            return {"ok": False, "message": "Core could not complete the diagnostic report."}
        response = json.loads(receipt.read_text(encoding="utf-8-sig"))
        if not isinstance(response, dict) or response.get("ok") is not True:
            return {"ok": False, "message": str(response.get("message", "Report failed")) if isinstance(response, dict) else "Invalid report response"}
        path = Path(response.get("path", ""))
        if not path.is_absolute() or not path.is_file() or path.suffix.lower() != ".txt":
            return {"ok": False, "message": "The diagnostic report file is missing."}
        with path.open(encoding="utf-8-sig") as report:
            if "PLAYHUB DIAGNOSTIC REPORT" not in report.read(256):
                return {"ok": False, "message": "Unexpected diagnostic report format."}
        return {"ok": True, "path": str(path)}
    except subprocess.TimeoutExpired:
        return {"ok": False, "message": "Diagnostic generation timed out. Try the Core app."}
    except (OSError, ValueError, KeyError, TypeError) as error:
        return {"ok": False, "message": "Diagnostic generation failed: " + str(error)}
    finally:
        if receipt is not None:
            try:
                receipt.unlink(missing_ok=True)
            except OSError:
                pass
        _gate.release()
