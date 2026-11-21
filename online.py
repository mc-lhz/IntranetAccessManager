"""判断当前是否已经可以正常访问互联网。"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

import requests

from netutil import (
    decode_html,
    find_portal_url,
    is_portal_body,
    normalize_base_url,
    origin,
)


PROBE_URLS = (
    "http://connect.rom.miui.com/generate_204",
    "http://www.baidu.com/",
)


@dataclass
class OnlineCheckResult:
    online: bool
    network_ready: bool
    portal_url: str
    detail: str


def _new_session() -> requests.Session:
    session = requests.Session()
    session.trust_env = False
    session.proxies = {}
    session.headers.update(
        {
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/142.0.0.0 Safari/537.36"
            ),
            "Accept": "text/html,application/json,*/*;q=0.8",
        }
    )
    return session


def check(configured_portal: str = "") -> OnlineCheckResult:
    portal = normalize_base_url(configured_portal)
    last_error = ""
    got_response = False
    session = _new_session()

    for url in PROBE_URLS:
        try:
            response = session.get(url, timeout=(5, 7), allow_redirects=False)
            got_response = True
            status = response.status_code

            if status == 204:
                return OnlineCheckResult(
                    True, True, portal, f"网络已连接（{url} 返回 204）"
                )

            if 300 <= status < 400:
                location = response.headers.get("Location", "")
                detected = origin(location)
                if detected:
                    portal = detected
                return OnlineCheckResult(
                    False, True, portal, f"检测到入网重定向：{location or '(无 Location)'}"
                )

            if status == 200:
                body = decode_html(response)
                if is_portal_body(body):
                    detected = find_portal_url(body, url)
                    if detected:
                        portal = detected
                    return OnlineCheckResult(False, True, portal, "检测到入网认证页面")

                if "baidu" in url and ("baidu" in body.lower() or "百度" in body):
                    return OnlineCheckResult(True, True, portal, "网络已连接（百度探测通过）")
        except requests.RequestException as exc:
            last_error = f"{type(exc).__name__}: {exc}"
        except Exception as exc:  # noqa: BLE001
            last_error = str(exc)

    detail = f"网络未认证（{last_error}）" if got_response else f"网络尚未就绪（{last_error}）"
    return OnlineCheckResult(False, got_response, portal, detail)


def discover_portal(configured_portal: str = "") -> str:
    """优先使用配置地址；没有配置时从探测重定向中提取。"""
    portal = normalize_base_url(configured_portal)
    if configured_portal and configured_portal.strip():
        return portal

    session = _new_session()
    for url in PROBE_URLS:
        try:
            response = session.get(url, timeout=(4, 5), allow_redirects=False)
            if 300 <= response.status_code < 400:
                detected = origin(response.headers.get("Location", ""))
                if detected:
                    return detected
            body = decode_html(response)
            detected = find_portal_url(body, url)
            if detected:
                return detected
        except Exception:
            continue
    return portal