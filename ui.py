"""Tkinter 设置界面、托盘图标与主应用。"""

from __future__ import annotations

import logging
import os
import queue
import re
import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import messagebox, ttk
from typing import Callable, Optional
from urllib.parse import urlparse

import pystray
from PIL import Image, ImageDraw

import autostart
from config import AppConfig, log_dir
from online import check
from portal import LoginResult, PortalClient
from worker import (
    LoginWorker,
    STATE_ERROR,
    STATE_LOGGING_IN,
    STATE_NEEDS_SETUP,
    STATE_ONLINE,
    STATE_STARTING,
    STATE_WAITING_NETWORK,
)

LOGGER = logging.getLogger(__name__)

BLUE = "#1e6fd9"
BLUE_LIGHT = "#488fe8"
TEXT = "#2d3746"
MUTED = "#8a8f99"
GREEN = "#168c50"
RED = "#b3261e"
WHITE = "#ffffff"
BG = "#f8f9fb"


def resource_path(relative: str) -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base / relative


def load_icon_image() -> Image.Image:
    icon_path = resource_path("assets/app.ico")
    if icon_path.exists():
        try:
            return Image.open(icon_path).convert("RGBA")
        except Exception:
            pass
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle([2, 2, 62, 62], radius=14, fill=BLUE)
    draw.line([(15, 33), (28, 46), (50, 18)], fill="white", width=7)
    return image


class SettingsWindow(tk.Toplevel):
    def __init__(
        self,
        master: tk.Misc,
        config: AppConfig,
        first_run: bool = False,
        on_saved: Optional[Callable[[AppConfig], None]] = None,
    ):
        super().__init__(master)
        self.config = config.clone()
        self.first_run = first_run
        self.on_saved = on_saved
        self.saved_config: Optional[AppConfig] = None
        self._testing = False
        self._test_queue: "queue.Queue[LoginResult]" = queue.Queue()
        self._initial_password = config.password

        self.title("校园网自动登录 - 首次设置" if first_run else "校园网自动登录 - 设置")
        self.configure(bg=WHITE)
        self.resizable(False, False)
        self.protocol("WM_DELETE_WINDOW", self._cancel)
        self._build()
        self._center(660, 536)
        try:
            if master.winfo_viewable():
                self.transient(master)
        except Exception:
            pass
        self.after(80, self._activate)

    def _build(self) -> None:
        header = tk.Frame(self, bg=BLUE, height=88)
        header.pack(side="top", fill="x")
        header.pack_propagate(False)
        tk.Label(
            header,
            text="校园网自动登录",
            bg=BLUE,
            fg=WHITE,
            font=("Microsoft YaHei UI", 18, "bold"),
        ).place(x=28, y=15)
        tk.Label(
            header,
            text="厦门市教育城域网 · 入网认证",
            bg=BLUE,
            fg="#dceaff",
            font=("Microsoft YaHei UI", 9),
        ).place(x=30, y=55)
        badge = tk.Label(
            header,
            text="安全登录",
            bg=BLUE_LIGHT,
            fg=WHITE,
            font=("Microsoft YaHei UI", 8),
            width=9,
            height=1,
        )
        badge.place(x=548, y=30)

        footer = tk.Frame(self, bg=BG, height=64)
        footer.pack(side="bottom", fill="x")
        footer.pack_propagate(False)

        body = tk.Frame(self, bg=WHITE)
        body.pack(side="top", fill="both", expand=True, padx=28, pady=16)
        body.grid_columnconfigure(1, weight=1)

        self.portal_var = tk.StringVar(value=self.config.portal_url)
        self.username_var = tk.StringVar(value=self.config.username)
        self.password_var = tk.StringVar(value=self.config.password)
        self.auto_var = tk.BooleanVar(value=bool(self.config.auto_start or autostart.is_enabled()))
        self.show_password_var = tk.BooleanVar(value=False)

        row = 0
        tk.Label(body, text="认证地址", bg=WHITE, fg=TEXT, font=("Microsoft YaHei UI", 10)).grid(
            row=row, column=0, sticky="w", pady=(2, 6)
        )
        self.portal_entry = ttk.Entry(body, textvariable=self.portal_var, width=42, font=("Microsoft YaHei UI", 10))
        self.portal_entry.grid(row=row, column=1, sticky="ew", padx=(10, 8), pady=(2, 6))
        tk.Label(body, text="默认已填好", bg=WHITE, fg=MUTED, font=("Microsoft YaHei UI", 8)).grid(
            row=row, column=2, sticky="w", pady=(2, 6)
        )

        row += 1
        tk.Label(body, text="手机号", bg=WHITE, fg=TEXT, font=("Microsoft YaHei UI", 10)).grid(
            row=row, column=0, sticky="w", pady=6
        )
        self.username_entry = ttk.Entry(body, textvariable=self.username_var, width=42, font=("Microsoft YaHei UI", 10))
        self.username_entry.grid(row=row, column=1, sticky="ew", padx=(10, 8), pady=6)
        tk.Label(body, text="I教育平台账号", bg=WHITE, fg=MUTED, font=("Microsoft YaHei UI", 8)).grid(
            row=row, column=2, sticky="w", pady=6
        )

        row += 1
        tk.Label(body, text="密码", bg=WHITE, fg=TEXT, font=("Microsoft YaHei UI", 10)).grid(
            row=row, column=0, sticky="w", pady=6
        )
        password_frame = tk.Frame(body, bg=WHITE)
        password_frame.grid(row=row, column=1, sticky="ew", padx=(10, 8), pady=6)
        self.password_entry = ttk.Entry(
            password_frame,
            textvariable=self.password_var,
            width=34,
            show="*",
            font=("Microsoft YaHei UI", 10),
        )
        self.password_entry.pack(side="left", fill="x", expand=True)
        self.show_check = tk.Checkbutton(
            password_frame,
            text="显示",
            variable=self.show_password_var,
            command=self._toggle_password,
            bg=WHITE,
            activebackground=WHITE,
            fg=MUTED,
            font=("Microsoft YaHei UI", 8),
        )
        self.show_check.pack(side="right", padx=(8, 0))
        tk.Label(body, text="仅保存在本机", bg=WHITE, fg=MUTED, font=("Microsoft YaHei UI", 8)).grid(
            row=row, column=2, sticky="w", pady=6
        )

        row += 1
        self.auto_check = tk.Checkbutton(
            body,
            text="登录 Windows 后自动启动",
            variable=self.auto_var,
            bg=WHITE,
            activebackground=WHITE,
            fg=TEXT,
            font=("Microsoft YaHei UI", 10),
        )
        self.auto_check.grid(row=row, column=1, sticky="w", padx=(10, 0), pady=(4, 2))
        tk.Label(body, text="推荐开启", bg=WHITE, fg=MUTED, font=("Microsoft YaHei UI", 8)).grid(
            row=row, column=2, sticky="w", pady=(4, 2)
        )

        row += 1
        self.status_label = tk.Label(
            body,
            text=(
                "首次使用：请输入 I教育平台 的手机号和密码，然后点击“保存”。"
                if self.first_run
                else "修改后点击“保存”，程序会在需要时自动登录。"
            ),
            bg=WHITE,
            fg="#5a5a5a",
            anchor="w",
            justify="left",
            wraplength=580,
            font=("Microsoft YaHei UI", 9),
        )
        self.status_label.grid(row=row, column=0, columnspan=3, sticky="w", pady=(14, 4))

        row += 1
        tk.Label(
            body,
            text="密码使用 Windows DPAPI 加密后保存在本机，不会上传到其他服务器。",
            bg=WHITE,
            fg=MUTED,
            anchor="w",
            font=("Microsoft YaHei UI", 8),
        ).grid(row=row, column=0, columnspan=3, sticky="w", pady=(8, 0))

        row += 1
        tk.Label(
            body,
            text="本程序仅自动提交学校官方入网页面，不绕过认证、设备检查或任何安全策略。",
            bg=WHITE,
            fg=MUTED,
            anchor="w",
            font=("Microsoft YaHei UI", 8),
        ).grid(row=row, column=0, columnspan=3, sticky="w", pady=(2, 0))

        row += 1
        tip = tk.Frame(
            body,
            bg="#f1f7ff",
            highlightbackground="#d8e8fb",
            highlightthickness=1,
        )
        tip.grid(row=row, column=0, columnspan=3, sticky="ew", pady=(14, 0))
        tk.Label(
            tip,
            text="使用提示",
            bg="#f1f7ff",
            fg=BLUE,
            anchor="w",
            font=("Microsoft YaHei UI", 9, "bold"),
        ).pack(anchor="w", padx=12, pady=(8, 2))
        tk.Label(
            tip,
            text=(
                "如果测试时提示“当前网络已经在线”，说明无需重复登录，直接保存即可。\n"
                "开机自启是“登录 Windows 后启动”；如学校要求输密码前就联网，请改用任务计划程序。"
            ),
            bg="#f1f7ff",
            fg="#52657a",
            anchor="w",
            justify="left",
            wraplength=570,
            font=("Microsoft YaHei UI", 8),
        ).pack(anchor="w", padx=12, pady=(0, 8))

        self.test_button = tk.Button(
            footer,
            text="测试登录",
            command=self._test,
            width=12,
            height=1,
            relief="flat",
            bg=WHITE,
            fg=TEXT,
            activebackground="#eef2f8",
            font=("Microsoft YaHei UI", 9),
        )
        self.test_button.place(x=28, y=16)

        self.save_button = tk.Button(
            footer,
            text="保存",
            command=self._save,
            width=10,
            height=1,
            relief="flat",
            bg=BLUE,
            fg=WHITE,
            activebackground="#195fbc",
            activeforeground=WHITE,
            font=("Microsoft YaHei UI", 9),
        )
        self.save_button.place(x=440, y=16)

        self.cancel_button = tk.Button(
            footer,
            text="取消",
            command=self._cancel,
            width=10,
            height=1,
            relief="flat",
            bg=WHITE,
            fg=TEXT,
            activebackground="#eef2f8",
            font=("Microsoft YaHei UI", 9),
        )
        self.cancel_button.place(x=540, y=16)

        self.bind("<Return>", lambda event: self._save())
        self.bind("<Escape>", lambda event: self._cancel())

    def _center(self, width: int, height: int) -> None:
        self.update_idletasks()
        screen_w = self.winfo_screenwidth()
        screen_h = self.winfo_screenheight()
        x = max(0, (screen_w - width) // 2)
        y = max(0, (screen_h - height) // 2)
        self.geometry(f"{width}x{height}+{x}+{y}")

    def _activate(self) -> None:
        try:
            self.deiconify()
            self.lift()
            self.attributes("-topmost", True)
            self.after(250, lambda: self.attributes("-topmost", False))
            self.focus_force()
            self.username_entry.focus_set()
            try:
                self.grab_set()
            except Exception:
                self.after(120, self._grab_later)
        except Exception:
            pass

    def _grab_later(self) -> None:
        try:
            self.grab_set()
        except Exception:
            pass

    def _toggle_password(self) -> None:
        self.password_entry.configure(show="" if self.show_password_var.get() else "*")

    def _current_password(self) -> str:
        value = self.password_var.get()
        if value:
            return value
        return self._initial_password

    def _validated(self) -> tuple[bool, str]:
        portal = self.portal_var.get().strip() or "http://10.110.125.20"
        if not portal.lower().startswith(("http://", "https://")):
            portal = "http://" + portal
        parsed = urlparse(portal)
        if not parsed.netloc:
            return False, "认证地址格式不正确。"
        if not self.username_var.get().strip():
            return False, "请输入 I教育平台手机号。"
        if not self._current_password():
            return False, "请输入 I教育平台密码。"
        return True, ""

    def _set_status(self, text: str, color: str) -> None:
        self.status_label.configure(text=text, fg=color)

    def _test(self) -> None:
        if self._testing:
            return
        ok, message = self._validated()
        if not ok:
            self._set_status(message, RED)
            return

        portal = normalize_portal(self.portal_var.get())
        username = self.username_var.get().strip()
        password = self._current_password()

        self._testing = True
        self.test_button.configure(state="disabled")
        self.save_button.configure(state="disabled")
        self._set_status("正在连接认证服务器，请稍候...", BLUE)

        def worker() -> None:
            try:
                online = check(portal)
                if online.online:
                    result = LoginResult(True, "当前网络已经在线，无需重复登录。")
                else:
                    client = PortalClient(_temp_config(self.config, portal, username, password))
                    result = client.login(username, password)
                    client.close()
            except Exception as exc:  # noqa: BLE001
                result = LoginResult(False, f"测试失败：{exc}", "EXCEPTION")
            self._test_queue.put(result)

        threading.Thread(target=worker, name="SettingsTest", daemon=True).start()
        self.after(100, self._poll_test)

    def _poll_test(self) -> None:
        if not self._testing:
            return
        try:
            result = self._test_queue.get_nowait()
        except queue.Empty:
            try:
                self.after(100, self._poll_test)
            except Exception:
                pass
            return
        self._test_done(result)

    def _test_done(self, result: LoginResult) -> None:
        if not self.winfo_exists():
            return
        self._testing = False
        self.test_button.configure(state="normal")
        self.save_button.configure(state="normal")
        self._set_status(result.message, GREEN if result.success else RED)

    def _save(self) -> None:
        if self._testing:
            return
        ok, message = self._validated()
        if not ok:
            self._set_status(message, RED)
            return

        self.config.portal_url = normalize_portal(self.portal_var.get())
        self.config.username = self.username_var.get().strip()
        password = self._current_password()
        if password:
            self.config.set_password(password)
        self.config.auto_start = bool(self.auto_var.get())

        try:
            self.config.save()
            autostart.set_enabled(self.config.auto_start)
        except Exception as exc:  # noqa: BLE001
            self._set_status(f"保存失败：{exc}", RED)
            return

        self.saved_config = self.config
        if self.on_saved:
            try:
                self.on_saved(self.config)
            except Exception as exc:  # noqa: BLE001
                LOGGER.warning("保存回调失败: %s", exc)
        self.destroy()

    def _cancel(self) -> None:
        self.destroy()


def normalize_portal(value: str) -> str:
    value = (value or "").strip() or "http://10.110.125.20"
    if not value.lower().startswith(("http://", "https://")):
        value = "http://" + value
    parsed = urlparse(value)
    if not parsed.netloc:
        return "http://10.110.125.20"
    return f"{parsed.scheme}://{parsed.netloc}"


def _temp_config(config: AppConfig, portal: str, username: str, password: str) -> AppConfig:
    temp = config.clone()
    temp.portal_url = portal
    temp.username = username
    temp.password = password
    return temp


class CampusApp:
    def __init__(self, config: AppConfig, first_run: bool = False):
        self.config = config.clone()
        self.first_run = first_run
        self.root = tk.Tk()
        self.root.withdraw()
        self.root.title("校园网自动登录")
        try:
            icon_path = resource_path("assets/app.ico")
            if icon_path.exists():
                self.root.iconbitmap(str(icon_path))
        except Exception:
            pass

        self.ui_queue: "queue.Queue[Callable[[], None]]" = queue.Queue()
        self.worker: Optional[LoginWorker] = None
        self.tray: Optional[pystray.Icon] = None
        self.tray_created = False
        self.fallback_window: Optional[tk.Toplevel] = None
        self.fallback_label: Optional[tk.Label] = None
        self._settings_open = False
        self._exiting = False
        self.status_state = STATE_STARTING
        self.status_message = "正在启动..."
        self.root.after(100, self._drain_queue)

    def run(self) -> None:
        if self.first_run:
            window = SettingsWindow(self.root, self.config, first_run=True)
            self.root.wait_window(window)
            if window.saved_config is None:
                if not self.config.has_credentials:
                    self.root.destroy()
                    return
            else:
                self.config = window.saved_config
            self.first_run = False

        if not self.config.has_credentials:
            self.root.destroy()
            return

        self._create_tray()
        self.worker = LoginWorker(self.config, self._status_callback, self._notify_callback)
        self.worker.start()
        self._set_status(STATE_STARTING, "正在启动...")
        try:
            self.root.mainloop()
        finally:
            if self.worker is not None:
                self.worker.stop()
            if self.tray is not None:
                try:
                    self.tray.stop()
                except Exception:
                    pass
            try:
                self.root.destroy()
            except Exception:
                pass

    # ------------------------------------------------------------------ 托盘
    def _create_tray(self) -> None:
        try:
            image = load_icon_image()
            self.tray = pystray.Icon(
                "CampusNetLogin",
                image,
                "校园网自动登录",
                self._build_menu(),
            )
            self.tray.run_detached()
            self.tray_created = True
        except Exception as exc:  # noqa: BLE001
            LOGGER.exception("创建托盘图标失败: %s", exc)
            self.tray = None
            self._create_fallback_window()

    def _build_menu(self) -> pystray.Menu:
        return pystray.Menu(
            pystray.MenuItem(lambda item: self._status_text(), None, enabled=False),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("立即检查 / 登录", lambda icon, item: self._enqueue(self._trigger_login)),
            pystray.MenuItem("设置...", lambda icon, item: self._enqueue(self._show_settings)),
            pystray.MenuItem("打开日志", lambda icon, item: self._enqueue(self._open_logs)),
            pystray.MenuItem(
                "登录 Windows 后自动启动",
                lambda icon, item: self._enqueue(self._toggle_autostart),
                checked=lambda item: autostart.is_enabled(),
            ),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("退出", lambda icon, item: self._enqueue(self._exit)),
        )

    def _status_text(self) -> str:
        return f"状态：{state_text(self.status_state)}  {self.status_message}"

    def _enqueue(self, callback: Callable[[], None]) -> None:
        self.ui_queue.put(callback)

    def _drain_queue(self) -> None:
        while True:
            try:
                callback = self.ui_queue.get_nowait()
            except queue.Empty:
                break
            try:
                callback()
            except Exception as exc:  # noqa: BLE001
                LOGGER.exception("界面回调异常: %s", exc)
        if not self._exiting:
            self.root.after(100, self._drain_queue)

    def _status_callback(self, state: str, message: str) -> None:
        self._enqueue(lambda: self._set_status(state, message))

    def _notify_callback(self, message: str) -> None:
        self._enqueue(lambda: self._notify(message))

    def _set_status(self, state: str, message: str) -> None:
        self.status_state = state
        self.status_message = message
        if self.tray is not None:
            try:
                title = f"校园网自动登录 - {state_text(state)}"
                self.tray.title = title[:63]
                self.tray.update_menu()
            except Exception:
                pass
        if self.fallback_label is not None:
            try:
                self.fallback_label.configure(text=f"{state_text(state)}\n{message}")
            except Exception:
                pass

    def _notify(self, message: str) -> None:
        if self.tray is None:
            return
        try:
            self.tray.notify(message, "校园网自动登录")
        except Exception as exc:  # noqa: BLE001
            LOGGER.warning("托盘通知失败: %s", exc)

    def _trigger_login(self) -> None:
        if self.worker is not None:
            self.worker.trigger()
            self._notify("正在检查网络状态...")

    def _show_settings(self) -> None:
        if self._settings_open:
            return
        self._settings_open = True
        try:
            window = SettingsWindow(self.root, self.config, first_run=False, on_saved=self._config_saved)
            self.root.wait_window(window)
        finally:
            self._settings_open = False

    def _config_saved(self, config: AppConfig) -> None:
        self.config = config.clone()
        if self.worker is not None:
            self.worker.update_config(self.config)
        if self.tray is not None:
            try:
                self.tray.update_menu()
            except Exception:
                pass
        self._notify("设置已保存，正在应用新配置。")

    def _toggle_autostart(self) -> None:
        try:
            enabled = not autostart.is_enabled()
            autostart.set_enabled(enabled)
            self.config.auto_start = enabled
            self.config.save()
            if self.tray is not None:
                self.tray.update_menu()
            self._notify("已开启登录时自动启动。" if enabled else "已取消自动启动。")
        except Exception as exc:  # noqa: BLE001
            messagebox.showerror("校园网自动登录", f"设置自动启动失败：{exc}")

    def _open_logs(self) -> None:
        try:
            os.startfile(str(log_dir()))
        except Exception as exc:  # noqa: BLE001
            messagebox.showerror("校园网自动登录", f"打开日志目录失败：{exc}")

    def _exit(self) -> None:
        if self._exiting:
            return
        self._exiting = True
        if self.worker is not None:
            self.worker.stop()
        if self.tray is not None:
            try:
                self.tray.stop()
            except Exception:
                pass
            self.tray = None
        try:
            self.root.quit()
        except Exception:
            pass

    def _create_fallback_window(self) -> None:
        self.fallback_window = tk.Toplevel(self.root)
        self.fallback_window.title("校园网自动登录")
        self.fallback_window.geometry("460x200")
        self.fallback_window.configure(bg=WHITE)
        self.fallback_window.protocol("WM_DELETE_WINDOW", lambda: self._enqueue(self._exit))
        tk.Label(
            self.fallback_window,
            text="校园网自动登录",
            bg=WHITE,
            fg=TEXT,
            font=("Microsoft YaHei UI", 14, "bold"),
        ).pack(anchor="w", padx=20, pady=(18, 4))
        self.fallback_label = tk.Label(
            self.fallback_window,
            text="正在启动...",
            bg=WHITE,
            fg=MUTED,
            anchor="w",
            justify="left",
            wraplength=410,
            font=("Microsoft YaHei UI", 9),
        )
        self.fallback_label.pack(anchor="w", padx=20, pady=4)
        buttons = tk.Frame(self.fallback_window, bg=WHITE)
        buttons.pack(anchor="w", padx=20, pady=12)
        tk.Button(buttons, text="设置", command=lambda: self._enqueue(self._show_settings)).pack(side="left")
        tk.Button(buttons, text="立即登录", command=lambda: self._enqueue(self._trigger_login)).pack(side="left", padx=8)
        tk.Button(buttons, text="退出", command=lambda: self._enqueue(self._exit)).pack(side="left")


def state_text(state: str) -> str:
    return {
        STATE_ONLINE: "已连接",
        STATE_LOGGING_IN: "正在登录",
        STATE_ERROR: "登录失败",
        STATE_WAITING_NETWORK: "等待网络",
        STATE_NEEDS_SETUP: "需要设置",
        STATE_STARTING: "启动中",
    }.get(state, "启动中")