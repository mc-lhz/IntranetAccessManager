"""后台检查/登录线程。"""

from __future__ import annotations

import logging
import threading
import time
from datetime import datetime
from typing import Callable

from config import AppConfig
from online import check
from portal import PortalClient

LOGGER = logging.getLogger(__name__)

STATE_STARTING = "starting"
STATE_ONLINE = "online"
STATE_LOGGING_IN = "logging_in"
STATE_ERROR = "error"
STATE_WAITING_NETWORK = "waiting_network"
STATE_NEEDS_SETUP = "needs_setup"


class LoginWorker(threading.Thread):
    def __init__(
        self,
        config: AppConfig,
        on_status: Callable[[str, str], None],
        on_notify: Callable[[str], None],
    ):
        super().__init__(name="CampusNetLoginWorker", daemon=True)
        self._config = config.clone()
        self._on_status = on_status
        self._on_notify = on_notify
        self._stop_event = threading.Event()
        self._wake_event = threading.Event()
        self._last_state = ""
        self._last_message = ""
        self._last_notification = 0.0

    def update_config(self, config: AppConfig) -> None:
        self._config = config.clone()
        self._wake_event.set()

    def trigger(self) -> None:
        self._wake_event.set()

    def stop(self) -> None:
        self._stop_event.set()
        self._wake_event.set()
        self.join(timeout=4)

    def run(self) -> None:
        LOGGER.info("登录服务已启动")
        while not self._stop_event.is_set():
            config = self._config.clone()
            if not config.has_credentials:
                self._set_status(STATE_NEEDS_SETUP, "请先填写手机号和密码")
                self._wait(30)
                continue

            result = check(config.portal_url)
            if self._stop_event.is_set():
                break

            if result.online:
                self._set_status(STATE_ONLINE, "网络已连接")
                self._wait(config.check_interval_seconds)
                continue

            if not result.network_ready:
                self._set_status(STATE_WAITING_NETWORK, "等待网络连接...")
                self._wait(10)
                continue

            self._set_status(STATE_LOGGING_IN, "正在登录校园网...")
            client = PortalClient(config)
            login_result = client.login(config.username, config.password)
            try:
                client.close()
            except Exception:
                pass

            if self._stop_event.is_set():
                break

            if login_result.success:
                if self._wait_for_online(config, 12):
                    config.last_success = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                    if client.api_version:
                        config.api_version = client.api_version
                    try:
                        config.save()
                    except Exception as exc:  # noqa: BLE001
                        LOGGER.warning("保存成功状态失败: %s", exc)
                    self._set_status(STATE_ONLINE, "登录成功，网络已连接")
                    self._notify("校园网已连接")
                    self._wait(config.check_interval_seconds)
                else:
                    self._set_status(STATE_ERROR, "认证成功，但网络尚未恢复")
                    self._notify_throttled("认证成功但网络尚未恢复，请稍后重试。")
                    self._wait(config.retry_interval_seconds)
                continue

            self._set_status(STATE_ERROR, login_result.message)
            self._notify_throttled(f"登录失败：{login_result.message}")
            self._wait(config.retry_interval_seconds)

        LOGGER.info("登录服务已停止")

    def _wait_for_online(self, config: AppConfig, seconds: int) -> bool:
        end = time.monotonic() + seconds
        while not self._stop_event.is_set() and time.monotonic() < end:
            time.sleep(2)
            if check(config.portal_url).online:
                return True
        return False

    def _wait(self, seconds: int) -> None:
        if self._stop_event.is_set():
            return
        self._wake_event.wait(timeout=max(1, seconds))
        self._wake_event.clear()

    def _set_status(self, state: str, message: str) -> None:
        if state == self._last_state and message == self._last_message:
            return
        self._last_state = state
        self._last_message = message
        LOGGER.info("状态: %s - %s", state, message)
        try:
            self._on_status(state, message)
        except Exception as exc:  # noqa: BLE001
            LOGGER.warning("状态回调异常: %s", exc)

    def _notify(self, message: str) -> None:
        self._last_notification = time.monotonic()
        try:
            self._on_notify(message)
        except Exception:
            pass

    def _notify_throttled(self, message: str) -> None:
        if time.monotonic() - self._last_notification < 600:
            return
        self._notify(message)