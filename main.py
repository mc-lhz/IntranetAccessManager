"""校园网自动登录（Python 版）入口。

用法:
  python main.py                 启动托盘程序
  python main.py --setup         打开设置
  python main.py --diagnose      生成诊断报告（不弹窗）
  python main.py --test-login    用已保存账号测试登录（--quiet 不弹窗）
  python main.py --install-startup / --remove-startup
"""

from __future__ import annotations

import ctypes
import logging
import os
import sys
from ctypes import wintypes
from logging.handlers import TimedRotatingFileHandler

from autostart import is_enabled, set_enabled
from config import AppConfig, data_dir, log_dir

APP_TITLE = "校园网自动登录"
MUTEX_NAME = r"Local\CampusNetLogin_SingleInstance"
_mutex_handle = None


def setup_logging() -> None:
    logger = logging.getLogger()
    logger.setLevel(logging.INFO)
    for handler in list(logger.handlers):
        logger.removeHandler(handler)

    try:
        handler = TimedRotatingFileHandler(
            log_dir() / "app.log",
            when="midnight",
            backupCount=30,
            encoding="utf-8",
        )
        handler.setFormatter(
            logging.Formatter("%(asctime)s [%(levelname)s] %(name)s: %(message)s")
        )
        logger.addHandler(handler)
    except Exception:
        pass

    if sys.stderr is not None:
        console = logging.StreamHandler()
        console.setFormatter(logging.Formatter("%(levelname)s: %(message)s"))
        logger.addHandler(console)

    logging.getLogger("urllib3").setLevel(logging.WARNING)
    logging.getLogger("requests").setLevel(logging.WARNING)
    logging.getLogger("PIL").setLevel(logging.WARNING)


def set_dpi_awareness() -> None:
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass


def message_box(text: str, title: str = APP_TITLE, flags: int = 0x40) -> None:
    try:
        ctypes.windll.user32.MessageBoxW(None, text, title, flags)
    except Exception:
        pass


def acquire_single_instance() -> bool:
    global _mutex_handle
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    kernel32.CreateMutexW.restype = wintypes.HANDLE
    _mutex_handle = kernel32.CreateMutexW(None, False, MUTEX_NAME)
    return ctypes.get_last_error() != 183


def release_single_instance() -> None:
    global _mutex_handle
    if _mutex_handle:
        try:
            ctypes.windll.kernel32.CloseHandle(_mutex_handle)
        except Exception:
            pass
        _mutex_handle = None


def has_arg(args: list[str], name: str) -> bool:
    return any(arg.lower() == name.lower() for arg in args)


def main() -> None:
    setup_logging()
    set_dpi_awareness()
    args = sys.argv[1:]

    if has_arg(args, "--version"):
        message_box("校园网自动登录 1.0.0\nPython 版", APP_TITLE)
        return

    if has_arg(args, "--diagnose"):
        from diagnostics import run_diagnostics

        path = run_diagnostics()
        if sys.stdout is not None:
            print(path)
        return

    if has_arg(args, "--test-login"):
        from diagnostics import run_test_login

        success, text, path = run_test_login()
        if not has_arg(args, "--quiet"):
            message_box(f"{text}\n结果已保存到：\n{path}", APP_TITLE, 0x40 if success else 0x30)
        elif sys.stdout is not None:
            print(text)
        return

    if has_arg(args, "--tray-test"):
        from ui import CampusApp

        app = CampusApp(AppConfig.load(), first_run=False)
        app.root.after(2500, app._exit)
        app.run()
        text = (
            f"tray_created={app.tray_created}\n"
            f"status={app.status_state}\n"
            f"message={app.status_message}\n"
        )
        path = data_dir() / "tray-test.txt"
        path.write_text(text, encoding="utf-8")
        if sys.stdout is not None:
            print(text)
        return

    if has_arg(args, "--install-startup") or has_arg(args, "--remove-startup"):
        enable = has_arg(args, "--install-startup")
        try:
            set_enabled(enable)
            if not has_arg(args, "--quiet"):
                message_box("已设置登录 Windows 后自动启动。" if enable else "已取消自动启动。")
        except Exception as exc:  # noqa: BLE001
            if not has_arg(args, "--quiet"):
                message_box(f"操作失败：{exc}", APP_TITLE, 0x10)
        return

    if not acquire_single_instance():
        message_box("校园网自动登录已经在运行，请在任务栏右下角的托盘图标中操作。")
        return

    try:
        config = AppConfig.load()
        first_run = (
            not config.has_credentials
            or bool(config.password_error)
            or has_arg(args, "--setup")
        )
        if config.auto_start and not is_enabled():
            try:
                set_enabled(True)
            except Exception:
                pass

        from ui import CampusApp

        app = CampusApp(config, first_run=first_run)
        app.run()
    except Exception as exc:  # noqa: BLE001
        logging.getLogger(__name__).exception("程序启动失败")
        message_box(f"程序启动失败：{exc}\n\n日志：{log_dir() / 'app.log'}", APP_TITLE, 0x10)
    finally:
        release_single_instance()


if __name__ == "__main__":
    main()