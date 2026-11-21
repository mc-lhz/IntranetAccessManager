"""本机配置与 Windows DPAPI 加密。

密码只以 DPAPI（当前 Windows 用户）加密后的形式写入 config.json。
"""

from __future__ import annotations

import base64
import copy
import ctypes
import json
import os
from ctypes import wintypes
from dataclasses import asdict, dataclass, field, fields
from pathlib import Path
from typing import Any


APP_NAME = "CampusNetLogin"


def data_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA")
    if not base:
        base = str(Path.home() / "AppData" / "Local")
    path = Path(base) / APP_NAME
    path.mkdir(parents=True, exist_ok=True)
    return path


def log_dir() -> Path:
    path = data_dir() / "logs"
    path.mkdir(parents=True, exist_ok=True)
    return path


CONFIG_PATH = data_dir() / "config.json"


class DATA_BLOB(ctypes.Structure):
    _fields_ = [
        ("cbData", wintypes.DWORD),
        ("pbData", ctypes.POINTER(ctypes.c_byte)),
    ]


_crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

_crypt32.CryptProtectData.argtypes = [
    ctypes.POINTER(DATA_BLOB),
    wintypes.LPCWSTR,
    ctypes.POINTER(DATA_BLOB),
    ctypes.c_void_p,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(DATA_BLOB),
]
_crypt32.CryptProtectData.restype = wintypes.BOOL
_crypt32.CryptUnprotectData.argtypes = [
    ctypes.POINTER(DATA_BLOB),
    ctypes.POINTER(wintypes.LPWSTR),
    ctypes.POINTER(DATA_BLOB),
    ctypes.c_void_p,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(DATA_BLOB),
]
_crypt32.CryptUnprotectData.restype = wintypes.BOOL
_kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
_kernel32.LocalFree.restype = wintypes.HLOCAL

CRYPTPROTECT_UI_FORBIDDEN = 0x01


def _blob(data: bytes) -> tuple[DATA_BLOB, Any]:
    buffer = ctypes.create_string_buffer(data)
    blob = DATA_BLOB(
        len(data),
        ctypes.cast(buffer, ctypes.POINTER(ctypes.c_byte)),
    )
    return blob, buffer


def dpapi_protect(data: bytes) -> bytes:
    blob_in, _buffer = _blob(data)
    blob_out = DATA_BLOB()
    ok = _crypt32.CryptProtectData(
        ctypes.byref(blob_in),
        APP_NAME,
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(blob_out),
    )
    if not ok:
        raise OSError(ctypes.get_last_error(), "CryptProtectData failed")
    try:
        return ctypes.string_at(blob_out.pbData, blob_out.cbData)
    finally:
        _kernel32.LocalFree(ctypes.cast(blob_out.pbData, wintypes.HLOCAL))


def dpapi_unprotect(data: bytes) -> bytes:
    blob_in, _buffer = _blob(data)
    blob_out = DATA_BLOB()
    description = wintypes.LPWSTR()
    ok = _crypt32.CryptUnprotectData(
        ctypes.byref(blob_in),
        ctypes.byref(description),
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(blob_out),
    )
    if not ok:
        raise OSError(ctypes.get_last_error(), "CryptUnprotectData failed")
    try:
        return ctypes.string_at(blob_out.pbData, blob_out.cbData)
    finally:
        if description:
            _kernel32.LocalFree(ctypes.cast(description, wintypes.HLOCAL))
        _kernel32.LocalFree(ctypes.cast(blob_out.pbData, wintypes.HLOCAL))


@dataclass
class AppConfig:
    portal_url: str = "http://10.110.125.20"
    username: str = ""
    password_protected: str = ""
    auto_start: bool = True
    check_interval_seconds: int = 60
    retry_interval_seconds: int = 20
    api_version: str = "6039.3746"
    ascid: str = ""
    last_success: str = ""

    password: str = field(default="", repr=False, compare=False)
    password_error: str = field(default="", repr=False, compare=False)

    def clone(self) -> "AppConfig":
        return copy.deepcopy(self)

    @property
    def has_credentials(self) -> bool:
        return bool(self.username.strip() and self.password)

    def set_password(self, password: str) -> None:
        self.password = password or ""
        if not self.password:
            self.password_protected = ""
            return
        protected = dpapi_protect(self.password.encode("utf-8"))
        self.password_protected = base64.b64encode(protected).decode("ascii")

    def unprotect_password(self) -> bool:
        self.password_error = ""
        if self.password:
            return True
        if not self.password_protected:
            self.password = ""
            return False
        try:
            protected = base64.b64decode(self.password_protected)
            self.password = dpapi_unprotect(protected).decode("utf-8")
            return True
        except Exception as exc:  # noqa: BLE001
            self.password = ""
            self.password_error = "无法解密本机保存的密码，请重新输入。"
            return False

    @classmethod
    def load(cls) -> "AppConfig":
        config = cls()
        try:
            if CONFIG_PATH.exists():
                raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
                allowed = {f.name for f in fields(cls)}
                values = {k: v for k, v in raw.items() if k in allowed}
                config = cls(**values)
        except Exception:
            try:
                if CONFIG_PATH.exists():
                    backup = CONFIG_PATH.with_suffix(
                        f".bad-{__import__('datetime').datetime.now():%Y%m%d%H%M%S}.json"
                    )
                    CONFIG_PATH.replace(backup)
            except Exception:
                pass

        if not (config.portal_url or "").strip():
            config.portal_url = "http://10.110.125.20"
        if not (config.api_version or "").strip():
            config.api_version = "6039.3746"
        config.check_interval_seconds = max(15, int(config.check_interval_seconds or 60))
        config.retry_interval_seconds = max(10, int(config.retry_interval_seconds or 20))
        config.unprotect_password()
        return config

    def save(self) -> None:
        data = asdict(self)
        data.pop("password", None)
        data.pop("password_error", None)
        tmp = CONFIG_PATH.with_suffix(".tmp")
        tmp.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        os.replace(tmp, CONFIG_PATH)


def mask_username(username: str) -> str:
    username = (username or "").strip()
    if not username:
        return "(未设置)"
    if len(username) <= 4:
        return "****"
    if len(username) <= 8:
        return username[:2] + "****"
    return username[:3] + "****" + username[-4:]