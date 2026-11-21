"""HTTP / 门户地址相关的无依赖小工具。"""

from __future__ import annotations

import re
import time
from typing import Optional
from urllib.parse import urljoin, urlparse

import requests


PORTAL_MARKERS = (
    "入网规范管理平台",
    "access/ui",
    "index_redirec",
    "Infogo",
    "srun_portal",
    "深澜",
)


def now_ms() -> int:
    return int(time.time() * 1000)


def normalize_base_url(url: str) -> str:
    url = (url or "").strip()
    if not url:
        return "http://10.110.125.20"
    if not url.lower().startswith(("http://", "https://")):
        url = "http://" + url
    parsed = urlparse(url)
    if not parsed.scheme or not parsed.netloc:
        return "http://10.110.125.20"
    return f"{parsed.scheme}://{parsed.netloc}"


def origin(url: str) -> Optional[str]:
    if not url:
        return None
    try:
        parsed = urlparse(url)
        if parsed.scheme and parsed.netloc:
            return f"{parsed.scheme}://{parsed.netloc}"
    except Exception:
        pass
    return None


def is_portal_body(text: str) -> bool:
    if not text:
        return False
    return any(marker.lower() in text.lower() for marker in PORTAL_MARKERS)


def find_portal_url(text: str, base_url: str) -> Optional[str]:
    if not text:
        return None
    match = re.search(
        r"""<meta[^>]+http-equiv\s*=\s*["']?refresh["']?[^>]+content\s*=\s*["']?[^;]*;\s*url\s*=\s*([^"'>\s]+)""",
        text,
        re.IGNORECASE,
    )
    if match:
        return origin(urljoin(base_url, match.group(1).strip("\"'")))
    match = re.search(
        r"""(?:window\.)?location(?:\.href)?\s*=\s*["']([^"']+)["']""",
        text,
        re.IGNORECASE,
    )
    if match:
        return origin(urljoin(base_url, match.group(1)))
    return None


def decode_html(response: requests.Response, max_bytes: int = 65536) -> str:
    content = response.content[:max_bytes]
    charset = response.encoding
    if not charset or charset.lower() in ("iso-8859-1", "latin-1"):
        match = re.search(r"charset=([\w-]+)", response.headers.get("Content-Type", ""), re.I)
        if match:
            charset = match.group(1)
        else:
            match = re.search(rb"charset=[\"']?([\w-]+)", content, re.I)
            if match:
                charset = match.group(1).decode("ascii", "ignore")
    for candidate in (charset, "utf-8", "gbk"):
        if not candidate:
            continue
        try:
            return content.decode(candidate, errors="replace")
        except LookupError:
            continue
    return content.decode("utf-8", errors="replace")