from __future__ import annotations

import sys
import time
import tkinter as tk
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from config import AppConfig  # noqa: E402
from ui import SettingsWindow  # noqa: E402


def main() -> None:
    config = AppConfig()
    root = tk.Tk()
    root.withdraw()
    window = SettingsWindow(root, config, first_run=True)
    window.update()
    window.username_var.set("13800000000")
    window.password_var.set("Probe-Password-123")
    window._test()
    deadline = time.time() + 12
    while time.time() < deadline and window._testing:
        window.update()
        time.sleep(0.1)
    window.update()
    print("testing=", window._testing)
    print("status=", window.status_label.cget("text"))
    window.destroy()
    root.destroy()


if __name__ == "__main__":
    main()