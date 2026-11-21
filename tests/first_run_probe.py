from __future__ import annotations

import json
import logging
import sys
import tkinter as tk
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from config import AppConfig, CONFIG_PATH  # noqa: E402
from ui import CampusApp, SettingsWindow  # noqa: E402


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    try:
        CONFIG_PATH.unlink()
    except Exception:
        pass

    app = CampusApp(AppConfig(), first_run=True)

    def automate() -> None:
        try:
            windows = [w for w in app.root.winfo_children() if isinstance(w, SettingsWindow)]
            if not windows:
                app.root.after(100, automate)
                return
            window = windows[0]
            window.portal_var.set("http://10.110.125.20")
            window.username_var.set("13800000000")
            window.password_var.set("Probe-Password-123")
            window.auto_var.set(False)
            window._save()
            app.root.after(1600, app._exit)
        except Exception as exc:  # noqa: BLE001
            logging.exception("automate failed: %s", exc)
            app.root.after(0, app._exit)

    app.root.after(400, automate)
    app.run()

    result = {
        "tray": app.tray_created,
        "status_state": app.status_state,
        "config_exists": CONFIG_PATH.exists(),
        "username": AppConfig.load().username,
        "password_ok": AppConfig.load().password == "Probe-Password-123",
    }
    path = Path(__file__).resolve().parent / "first-run-result.json"
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(path)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    try:
        CONFIG_PATH.unlink()
    except Exception:
        pass


if __name__ == "__main__":
    main()