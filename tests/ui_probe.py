from __future__ import annotations

import json
import sys
import time
import tkinter as tk
from pathlib import Path

from PIL import ImageGrab

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from config import AppConfig  # noqa: E402
from ui import SettingsWindow  # noqa: E402


def main() -> None:
    config = AppConfig()
    config.username = "13800000000"
    config.password = "demo-password"
    root = tk.Tk()
    root.withdraw()
    window = SettingsWindow(root, config, first_run=True)
    window.update()
    time.sleep(0.45)
    window.update()
    window.update_idletasks()
    try:
        ImageGrab.grab().save(Path(__file__).resolve().parent / "ui.png")
    except Exception as exc:  # noqa: BLE001
        print(f"screenshot failed: {exc}")

    info = {
        "title": window.title(),
        "geometry": window.geometry(),
        "viewable": bool(window.winfo_viewable()),
        "mapped": bool(window.winfo_ismapped()),
        "screen": [window.winfo_screenwidth(), window.winfo_screenheight()],
        "status": window.status_label.cget("text"),
        "widgets": {},
    }
    for name in (
        "portal_entry",
        "username_entry",
        "password_entry",
        "auto_check",
        "test_button",
        "save_button",
        "cancel_button",
        "status_label",
    ):
        widget = getattr(window, name)
        info["widgets"][name] = {
            "x": widget.winfo_rootx(),
            "y": widget.winfo_rooty(),
            "w": widget.winfo_width(),
            "h": widget.winfo_height(),
            "mapped": bool(widget.winfo_ismapped()),
            "text": widget.cget("text") if "text" in widget.keys() else "",
        }
    info["grab"] = bool(window.grab_current())
    info["all"] = []

    def walk(parent: tk.Misc) -> None:
        for child in parent.winfo_children():
            info["all"].append(
                {
                    "class": child.winfo_class(),
                    "x": child.winfo_rootx(),
                    "y": child.winfo_rooty(),
                    "w": child.winfo_width(),
                    "h": child.winfo_height(),
                    "mapped": bool(child.winfo_ismapped()),
                    "text": child.cget("text") if "text" in child.keys() else "",
                }
            )
            walk(child)

    walk(window)
    path = Path(__file__).resolve().parent / "ui-result.json"
    path.write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
    print(path)
    window.destroy()
    root.destroy()


if __name__ == "__main__":
    main()