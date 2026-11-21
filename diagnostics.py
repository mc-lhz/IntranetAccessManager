"""诊断报告与命令行测试登录。"""

from __future__ import annotations

import logging
import platform
import sys
from datetime import datetime
from pathlib import Path

from config import AppConfig, CONFIG_PATH, data_dir, log_dir, mask_username
from online import check
from portal import PortalClient, rsa_encrypt

LOGGER = logging.getLogger(__name__)


def run_diagnostics() -> Path:
    lines: list[str] = []
    lines.append("校园网自动登录 - 诊断报告")
    lines.append(f"时间: {datetime.now():%Y-%m-%d %H:%M:%S}")
    lines.append(f"系统: {platform.platform()}")
    lines.append(f"Python: {sys.version.split()[0]}")
    lines.append(f"程序目录: {Path(sys.executable).resolve()}")
    lines.append(f"配置: {CONFIG_PATH}")
    lines.append("")

    config = AppConfig.load()
    lines.append(f"认证地址: {config.portal_url}")
    lines.append(f"账号: {mask_username(config.username)}")
    lines.append(f"已保存密码: {'是' if config.password else '否'}")
    try:
        import autostart

        lines.append(f"自动启动: {'是' if autostart.is_enabled() else '否'}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"自动启动: 读取失败 {exc}")
    lines.append("")

    lines.append("== 网络探测 ==")
    try:
        online = check(config.portal_url)
        lines.append(f"在线: {'是' if online.online else '否'}")
        lines.append(f"网络就绪: {'是' if online.network_ready else '否'}")
        lines.append(f"门户地址: {online.portal_url}")
        lines.append(f"说明: {online.detail}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"网络探测失败: {exc}")
    lines.append("")

    client = PortalClient(config)
    server = None
    lines.append("== 门户信息 ==")
    try:
        server = client.get_server_info(force=True)
        lines.append(f"门户标题: {server.title}")
        lines.append(f"引擎版本: {server.engine}")
        lines.append(f"API 版本: {server.api_version}")
        lines.append(f"公钥长度: {len(server.pub_key)}")
        lines.append(f"认证类型: {server.auth_server}")
        lines.append(f"账号提示: {server.client_user_hint}")
        lines.append(f"密码提示: {server.client_password_hint}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"读取门户信息失败: {exc}")
    lines.append("")

    lines.append("== 设备信息 ==")
    try:
        device = client.get_device_info(force=True)
        lines.append(f"设备 ID: {device.device_id}")
        lines.append(f"已注册: {'是' if device.registered else '否'}")
        lines.append(f"检查结果: {device.check_result}")
        lines.append(f"IP: {device.ip}")
        lines.append(f"MAC: {device.mac}")
        lines.append(f"AscID: {device.ascid}")
        lines.append(f"设备名: {device.dev_name}")
        lines.append(f"系统: {device.os_name}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"读取设备信息失败: {exc}")
    lines.append("")

    lines.append("== 加密自检 ==")
    try:
        if server is None or not server.pub_key:
            lines.append("跳过：未取得公钥")
        else:
            encrypted = rsa_encrypt("diagnose-test", server.pub_key)
            lines.append(f"RSA 加密成功，密文 Base64 长度: {len(encrypted)}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"RSA 加密失败: {exc}")
    finally:
        client.close()
    lines.append("")

    lines.append("== 最近日志 ==")
    try:
        log_path = Path(log_dir()) / "app.log"
        if log_path.exists():
            lines.extend(log_path.read_text(encoding="utf-8", errors="replace").splitlines()[-25:])
        else:
            lines.append("(暂无日志)")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"读取日志失败: {exc}")

    path = data_dir() / "diagnose.txt"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    LOGGER.info("诊断报告: %s", path)
    return path


def run_test_login() -> tuple[bool, str, Path]:
    config = AppConfig.load()
    if not config.has_credentials:
        return False, "尚未保存账号和密码，请先运行设置。", data_dir() / "login-test.txt"

    client = PortalClient(config)
    try:
        result = client.login(config.username, config.password)
    finally:
        client.close()

    text = (
        f"{datetime.now():%Y-%m-%d %H:%M:%S}\n"
        f"账号: {mask_username(config.username)}\n"
        f"成功: {'是' if result.success else '否'}\n"
        f"信息: {result.message}\n"
        f"错误码: {result.code}\n"
    )
    path = data_dir() / "login-test.txt"
    path.write_text(text, encoding="utf-8")
    return result.success, text, path