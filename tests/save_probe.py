from __future__ import annotations

import json
import sys
import time
import tkinter as tk
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import autostart  # noqa: E402
from config import AppConfig, CONFIG_PATH  # noqa: E402
from ui import SettingsWindow  # noqa: E402


def main() -> None:
    config = AppConfig()
    root = tk.Tk()
    root.withdraw()
    window = SettingsWindow(root, config, first_run=True)
    window.update()
    time.sleep(0.3)
    window.update()

    window.portal_var.set("10.110.125.20")
    window.username_var.set("13800000000")
    window.password_var.set("Probe-Password-123")
    window.auto_var.set(False)
    window._save()
    root.update()

    loaded = AppConfig.load()
    checks = {
        "config_exists": CONFIG_PATH.exists(),
        "portal": loaded.portal_url,
        "username": loaded.username,
        "password_ok": loaded.password == "Probe-Password-123",
        "protected_nonempty": bool(loaded.password_protected),
        "auto_start": loaded.auto_start,
        "registry_autostart": autostart.is_enabled(),
        "has_credentials": loaded.has_credentials,
    }
    path = Path(__file__).resolve().parent / "save-result.json"
    path.write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf-8")
    print(path)
    print(json.dumps(checks, ensure_ascii=False, indent=2))

    try:
        autostart.set_enabled(False)
    except Exception:
        pass
    try:
        CONFIG_PATH.unlink()
    except Exception:
        pass
    window.destroy()
    root.destroy()


if __name__ == "__main__":
    main()