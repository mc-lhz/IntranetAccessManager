from __future__ import annotations

import json
import logging
import sys
import tkinter as tk
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from config import AppConfig  # noqa: E402
from ui import CampusApp  # noqa: E402


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    config = AppConfig()
    config.portal_url = "http://10.110.125.20"
    config.username = "13800000000"
    config.password = "Probe-Password-123"
    config.auto_start = False

    app = CampusApp(config, first_run=False)
    app.root.after(3000, app._exit)
    app.run()

    result = {
        "tray": app.tray_created,
        "status_state": app.status_state,
        "status_message": app.status_message,
    }
    path = Path(__file__).resolve().parent / "tray-result.json"
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(path)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()