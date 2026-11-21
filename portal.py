"""盈高（Infogo）入网规范管理平台登录客户端。

只复现浏览器正常登录时提交的请求，不绕过认证、设备检查或安全策略。
"""

from __future__ import annotations

import base64
import hashlib
import json
import logging
import re
import secrets
import time
from dataclasses import dataclass, field
from typing import Any, Iterable, Optional
from urllib.parse import urlencode, urlparse

import requests

from config import AppConfig
from netutil import normalize_base_url, now_ms

LOGGER = logging.getLogger(__name__)

API_ROOT = "/access/1.0/windows/json/"
UI_PATH = "/access/ui/index.html"
USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/142.0.0.0 Safari/537.36"
)


class PortalError(Exception):
    def __init__(self, message: str, code: str = ""):
        super().__init__(message)
        self.code = code


@dataclass
class ServerInfo:
    portal_url: str
    engine: str = ""
    api_version: str = ""
    pub_key: str = ""
    title: str = ""
    client_user_hint: str = ""
    client_password_hint: str = ""
    auth_server: str = ""
    client_save_pass: bool = False
    need_auth: bool = True


@dataclass
class DeviceInfo:
    device_id: str
    registered: bool = False
    check_result: str = ""
    ip: str = ""
    mac: str = ""
    ascid: str = ""
    dev_name: str = ""
    os_name: str = ""


@dataclass
class LoginResult:
    success: bool
    message: str
    code: str = ""
    token: str = ""
    user_name: str = ""
    needs_two_factor: bool = False


def _truthy(value: Any) -> bool:
    return value is True or value == 1 or str(value).lower() in ("1", "true", "yes")


class PortalClient:
    def __init__(self, config: AppConfig):
        self.config = config.clone()
        self.base_url = normalize_base_url(config.portal_url)
        self.api_version = (config.api_version or "6039.3746").strip()
        self.ascid = (config.ascid or "").strip()
        self.session = requests.Session()
        self.session.trust_env = False
        self.session.proxies = {}
        self.session.headers.update(
            {
                "User-Agent": USER_AGENT,
                "Accept": "application/json, text/plain, */*",
                "Accept-Language": "zh-CN,zh;q=0.9",
                "X-Requested-With": "XMLHttpRequest",
            }
        )
        self._server: Optional[ServerInfo] = None
        self._server_at = 0.0
        self._device: Optional[DeviceInfo] = None
        self._device_at = 0.0

    def close(self) -> None:
        try:
            self.session.close()
        except Exception:
            pass

    # ------------------------------------------------------------------ 门户信息
    def get_server_info(self, force: bool = False) -> ServerInfo:
        if not force and self._server is not None and time.time() - self._server_at < 300:
            return self._server
        info = self._fetch_server_info()
        self._server = info
        self._server_at = time.time()
        return info

    def _fetch_server_info(self) -> ServerInfo:
        query = self._query()
        root = self._get_json("server/info", query)
        code = int(root.get("errcode") or 0)

        if code == 21101015 and self._refresh_api_version():
            root = self._get_json("server/info", self._query())
            code = int(root.get("errcode") or 0)

        if code != 0:
            raise PortalError(friendly_message(code, str(root.get("errmsg") or "")), str(code))

        data = root.get("data") or {}
        if not isinstance(data, dict):
            raise PortalError("认证服务器返回的数据不完整。", "NODATA")

        admin = data.get("ADMINISTRATORINFO") or {}
        client_auth = data.get("ClientAuth") or {}
        user = data.get("User") or {}
        client_check = data.get("CLIENTCHECK") or {}

        info = ServerInfo(
            portal_url=self.base_url,
            engine=str(data.get("Engine") or ""),
            api_version=self.api_version,
            pub_key=str(data.get("pubKey") or ""),
            title=str(admin.get("sTitle") or admin.get("indextitle") or ""),
            client_user_hint=str(client_auth.get("ClientUser") or ""),
            client_password_hint=str(client_auth.get("ClientPassword") or ""),
            auth_server=str(user.get("AuthServer") or ""),
            client_save_pass=_truthy(client_check.get("ClientSavePass")),
            need_auth=_truthy(client_check.get("IsAuth")),
        )
        return info

    # ------------------------------------------------------------------ 设备信息
    def get_device_info(self, force: bool = False) -> DeviceInfo:
        if not force and self._device is not None and time.time() - self._device_at < 300:
            return self._device
        info = self._fetch_device_info()
        self._device = info
        self._device_at = time.time()
        return info

    def _fetch_device_info(self) -> DeviceInfo:
        body: list[tuple[str, str]] = [
            ("gettype", "ipgetmac"),
            ("deviceid", ""),
            ("license", ""),
            ("UrlAswid", ""),
            ("BrowserFingerprint", ""),
            ("forbid", ""),
            ("basIP", ""),
            ("firstUrl", ""),
            ("Mac", ""),
            ("is_guest", "0"),
            ("is_mobile", "0"),
            ("os_platform", "Windows 10"),
            ("browser", "Chrome/142.0.0.0"),
            ("UrlAscid", self.ascid),
            ("wlanusermac", ""),
            ("doorFrom", ""),
            ("doorIP", ""),
            ("doorMAC", ""),
        ]
        query = f"?local_lguage_set=zh&cache={now_ms()}"
        root = self._post_json("device/infoprocess", query, body)
        code = int(root.get("errcode") or 0)
        if code != 0:
            raise PortalError(friendly_message(code, str(root.get("errmsg") or "")), str(code))

        data = root.get("data") or {}
        if not isinstance(data, dict):
            raise PortalError("认证服务器未返回设备信息。", "NODEVICE")
        device_id = str(data.get("DeviceID") or "")
        if not device_id:
            raise PortalError("认证服务器返回的设备标识为空，请稍后重试。", "NODEVICE")

        return DeviceInfo(
            device_id=device_id,
            registered=_truthy(data.get("Registered")),
            check_result=str(data.get("CheckResult") or ""),
            ip=str(data.get("IP") or ""),
            mac=str(data.get("Mac") or ""),
            ascid=str(data.get("AscID") or ""),
            dev_name=str(data.get("DevName") or ""),
            os_name=str(data.get("OSName") or ""),
        )

    # ------------------------------------------------------------------ 登录
    def login(self, username: str, password: str) -> LoginResult:
        try:
            return self._login(username, password, allow_version_retry=True)
        except PortalError as exc:
            LOGGER.warning("登录失败: %s (%s)", exc, exc.code)
            return LoginResult(False, str(exc), exc.code)
        except requests.RequestException as exc:
            LOGGER.warning("登录网络异常: %s", exc)
            return LoginResult(False, f"无法连接认证服务器：{exc}", "NETWORK")
        except Exception as exc:  # noqa: BLE001
            LOGGER.exception("登录异常")
            return LoginResult(False, f"登录异常：{exc}", "EXCEPTION")

    def _login(self, username: str, password: str, allow_version_retry: bool) -> LoginResult:
        server = self.get_server_info()
        if not server.pub_key:
            raise PortalError("认证服务器未返回加密公钥，请检查认证地址。", "NOPUBKEY")
        device = self.get_device_info()
        if not device.device_id:
            raise PortalError("未能获取本机设备标识，请确认已连接校园网。", "NODEVICE")

        encrypted = rsa_encrypt(password, server.pub_key)
        user_name64 = base64.b64encode((username or "").encode("utf-8")).decode("ascii")

        body: list[tuple[str, str]] = [
            ("user_name", user_name64),
            ("deviceid", device.device_id),
            ("idp", ""),
            ("tokenId", ""),
            ("remoteIp", ""),
            ("password", encrypted),
            ("hintOver", "0"),
            ("autoAuth", "0"),
            ("verify_code", ""),
            ("type", "User"),
        ]
        query = f"?local_lguage_set=zh&cache={now_ms()}"
        root = self._post_json("auth/index", query, body)
        code = int(root.get("errcode") or 0)

        if code == 0:
            data = root.get("data") or {}
            if isinstance(data, dict) and _truthy(data.get("FactorAuth")):
                return LoginResult(
                    False,
                    "该账号启用了二次验证，请打开认证网页完成短信/二次验证。",
                    "FactorAuth",
                    needs_two_factor=True,
                )
            return LoginResult(
                True,
                "登录成功",
                "",
                str(data.get("Token") or "") if isinstance(data, dict) else "",
                str(data.get("UserName") or "") if isinstance(data, dict) else "",
            )

        if code == 21101015 and allow_version_retry and self._refresh_api_version():
            return self._login(username, password, allow_version_retry=False)

        return LoginResult(False, friendly_message(code, str(root.get("errmsg") or "")), str(code))

    # ------------------------------------------------------------------ HTTP
    def _query(self) -> str:
        return (
            f"?basIP=&ascid={self.ascid}"
            f"&firsturl=&version={self.api_version}"
            f"&agentPromotion=&local_lguage_set=zh&cache={now_ms()}"
        )

    def _headers(self, referer: bool = True) -> dict[str, str]:
        headers = {
            "User-Agent": USER_AGENT,
            "Accept": "application/json, text/plain, */*",
            "Accept-Language": "zh-CN,zh;q=0.9",
            "X-Requested-With": "XMLHttpRequest",
        }
        if referer:
            headers["Referer"] = self.base_url + UI_PATH
        return headers

    def _get_json(self, api_path: str, query: str) -> dict[str, Any]:
        path_query = API_ROOT + api_path + query
        headers = self._headers()
        headers["Session-Num"] = session_num(path_query, None)
        response = self.session.get(
            self.base_url + path_query,
            headers=headers,
            timeout=(5, 15),
            allow_redirects=False,
        )
        return self._parse_response(response)

    def _post_json(
        self,
        api_path: str,
        query: str,
        body: Iterable[tuple[str, str]],
    ) -> dict[str, Any]:
        body_list = list(body)
        path_query = API_ROOT + api_path + query
        headers = self._headers()
        headers["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8"
        headers["Session-Num"] = session_num(path_query, body_list)
        encoded = urlencode(body_list, doseq=False)
        response = self.session.post(
            self.base_url + path_query,
            data=encoded.encode("utf-8"),
            headers=headers,
            timeout=(5, 15),
            allow_redirects=False,
        )
        return self._parse_response(response)

    def _parse_response(self, response: requests.Response) -> dict[str, Any]:
        if 300 <= response.status_code < 400:
            location = response.headers.get("Location", "")
            raise PortalError(f"认证服务器发生重定向：{location}", "REDIRECT")
        text = response.content.decode("utf-8", errors="replace")
        if not text.strip():
            raise PortalError(f"认证服务器返回空响应（HTTP {response.status_code}）。", "EMPTY")
        try:
            root = json.loads(text)
        except Exception as exc:  # noqa: BLE001
            LOGGER.warning("认证服务器返回非 JSON 内容: %s", text[:200])
            raise PortalError(f"认证服务器返回格式异常：{exc}", "BADJSON") from exc
        if not isinstance(root, dict):
            raise PortalError("认证服务器返回格式异常。", "BADJSON")
        return root

    def _refresh_api_version(self) -> bool:
        try:
            response = self.session.get(
                self.base_url + "/access/ui/js/index.js",
                timeout=(5, 25),
                allow_redirects=False,
            )
            text = response.content.decode("utf-8", errors="replace")
            match = re.search(
                r'getGlobalServerDispose[\s\S]{0,1200}?version\s*=\s*"([0-9]{3,5}\.[0-9]{3,5})"',
                text,
            )
            if not match:
                match = re.search(r'version\s*=\s*"([0-9]{3,5}\.[0-9]{3,5})"', text)
            if match:
                version = match.group(1)
                if version != self.api_version:
                    LOGGER.info("检测到门户版本更新: %s -> %s", self.api_version, version)
                    self.api_version = version
                    self._server = None
                    return True
        except Exception as exc:  # noqa: BLE001
            LOGGER.warning("自动获取门户版本失败: %s", exc)
        return False


# ---------------------------------------------------------------------- 工具函数
def session_num(path_query: str, body: Optional[Iterable[tuple[str, str]]]) -> str:
    raw = path_query
    if body:
        for key, value in body:
            raw += f"&{key}={value}"
    sanitized = re.sub(r"""[^0-9a-zA-Z=&()/._?'"<>]""", "", raw)
    return hashlib.md5(sanitized.encode("utf-8")).hexdigest()


def friendly_message(code: int, server_message: str = "") -> str:
    if code == 21100006:
        return "认证请求被拒绝（门户地址或版本不匹配，请检查认证地址）。"
    if code == 21101015:
        return "认证门户版本已更新，请稍后重试。"
    if code == 21134001:
        return "手机号不存在或尚未开通 I教育平台认证。"
    if code == 21134002:
        return "密码错误，请重新输入。"
    if code in (21133003, 21133015, 21133017):
        return "密码已过期，请先在认证网页修改密码。"
    if code == 21120030:
        return "需要在认证网页完成设备或账号确认。"
    return server_message or f"登录失败（错误码 {code}）。"


def _read_element(data: bytes, position: int) -> tuple[int, int, int, int]:
    if position < 0 or position + 2 > len(data):
        raise PortalError("公钥数据不完整。", "BADKEY")
    tag = data[position]
    position += 1
    first = data[position]
    position += 1
    if first & 0x80:
        count = first & 0x7F
        if count <= 0 or count > 4 or position + count > len(data):
            raise PortalError("公钥长度字段异常。", "BADKEY")
        length = int.from_bytes(data[position : position + count], "big")
        position += count
    else:
        length = first
    if length < 0 or position + length > len(data):
        raise PortalError("公钥内容越界。", "BADKEY")
    return tag, position, length, position + length


def parse_public_key(pem: str) -> tuple[int, int]:
    if not pem or "PUBLIC KEY" not in pem:
        raise PortalError("认证服务器公钥格式异常。", "BADKEY")
    pkcs1 = "RSA PUBLIC KEY" in pem
    base64_text = re.sub(r"-----[^-]+-----", "", pem)
    base64_text = re.sub(r"\s+", "", base64_text)
    try:
        der = base64.b64decode(base64_text, validate=True)
    except Exception as exc:  # noqa: BLE001
        raise PortalError("认证服务器公钥不是有效的 Base64。", "BADKEY") from exc

    _tag, outer_start, _outer_len, _outer_next = _read_element(der, 0)
    if pkcs1:
        _tag, mod_start, mod_len, mod_next = _read_element(der, outer_start)
        _tag, exp_start, exp_len, _ = _read_element(der, mod_next)
    else:
        _tag, _alg_start, _alg_len, alg_next = _read_element(der, outer_start)
        _tag, bit_start, _bit_len, _ = _read_element(der, alg_next)
        key_pos = bit_start + 1
        _tag, seq_start, _seq_len, _ = _read_element(der, key_pos)
        _tag, mod_start, mod_len, mod_next = _read_element(der, seq_start)
        _tag, exp_start, exp_len, _ = _read_element(der, mod_next)

    modulus = int.from_bytes(der[mod_start : mod_start + mod_len], "big")
    exponent = int.from_bytes(der[exp_start : exp_start + exp_len], "big")
    if modulus.bit_length() < 512 or exponent <= 1:
        raise PortalError("认证服务器公钥长度异常。", "BADKEY")
    return modulus, exponent


def rsa_encrypt(plain_text: str, pem: str) -> str:
    """RSA PKCS#1 v1.5 加密，与浏览器端 CryptoJS/JSEncrypt 的结果兼容。"""
    if not plain_text:
        raise PortalError("密码不能为空。", "PASSWORD")
    modulus, exponent = parse_public_key(pem)
    message = plain_text.encode("utf-8")
    key_size = (modulus.bit_length() + 7) // 8
    padding_length = key_size - len(message) - 3
    if padding_length < 8:
        raise PortalError("密码过长，无法使用门户公钥加密。", "PASSWORDTOOLONG")

    padding = bytearray()
    while len(padding) < padding_length:
        chunk = secrets.token_bytes(padding_length - len(padding))
        padding.extend(b for b in chunk if b != 0)
    encoded = b"\x00\x02" + bytes(padding[:padding_length]) + b"\x00" + message
    cipher = pow(int.from_bytes(encoded, "big"), exponent, modulus)
    return base64.b64encode(cipher.to_bytes(key_size, "big")).decode("ascii")