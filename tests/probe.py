from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from config import AppConfig, CONFIG_PATH, dpapi_protect, dpapi_unprotect  # noqa: E402
from online import check  # noqa: E402
from portal import PortalClient  # noqa: E402


def main() -> None:
    lines: list[str] = []
    config = AppConfig()
    config.portal_url = "http://10.110.125.20"
    config.username = "13800000000"
    config.password = "Test1234!"

    try:
        protected = dpapi_protect(b"hello-dpapi")
        assert dpapi_unprotect(protected) == b"hello-dpapi"
        lines.append("dpapi=ok")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"dpapi=FAIL {exc}")

    online = check(config.portal_url)
    lines.append(f"online={online.online} ready={online.network_ready} detail={online.detail}")

    client = PortalClient(config)
    try:
        server = client.get_server_info(True)
        lines.append(f"server={server.engine} pubkey={len(server.pub_key)} title={server.title}")
        device = client.get_device_info(True)
        lines.append(f"device={device.device_id} registered={device.registered} check={device.check_result}")
        result = client.login(config.username, config.password)
        lines.append(f"login success={result.success} code={result.code} message={result.message}")
    except Exception as exc:  # noqa: BLE001
        lines.append(f"EXCEPTION {type(exc).__name__}: {exc}")
    finally:
        client.close()

    path = Path(__file__).resolve().parent / "probe-result.txt"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(path)
    print("\n".join(lines))


if __name__ == "__main__":
    main()