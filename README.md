# 通用内网登录工具（Python 版）

面向 Windows 用户的校园网/教育城域网自动登录小工具。程序在后台检查网络，发现未认证时自动打开学校官方入网页面并提交登录；登录成功后驻留系统托盘，可设置为登录 Windows 后自动启动。

> 本工具只复现学校官方入网页面的正常登录请求，不绕过认证、设备检查、实名制或任何校园网安全策略。请在学校授权范围内使用。

---

## 1. 功能

- 自动检测当前是否已联网，已联网时不重复登录。
- 未联网时自动获取门户公钥、设备标识并登录。
- 登录失败自动重试，失败原因写入日志并在托盘提示。
- 密码使用 Windows DPAPI（当前用户）加密保存，不保存明文。
- 支持托盘菜单：立即登录、设置、打开日志、开机自启、退出。
- 支持首次运行向导、测试登录、诊断报告。
- 可打包为单文件 `CampusNetLogin.exe`，终端电脑不需要安装 Python。

---

## 2. 适用环境

- Windows 10 / Windows 11（64 位）。
- 典型应用场景：**地区教育城域网**或**高校校园网**入网认证。
- 认证门户：通常为内网地址（如 `http://10.x.x.x` 或 `http://172.x.x.x`），程序默认填好。
- 账号：**教育平台认证手机号**或**学号/工号**。
- 密码：**对应平台的登录密码**，不是操作系统密码。
- 需要在校园网有线/无线环境中使用；校外网络通常无法完成校内认证。

如果学校门户地址变更，在“设置”里修改“认证地址”即可。

---

## 3. 快速使用（推荐给普通用户）

1. 把 `dist\CampusNetLogin.exe` 放到一个固定目录，例如：
   `C:\Users\你的用户名\AppData\Local\Programs\CampusNetLogin\`
   不要放在“下载”临时目录，也不要在 U 盘里直接运行。
2. 双击 `CampusNetLogin.exe`。
3. 首次运行会弹出设置窗口：
   - 认证地址：默认填入的学校门户 URL；
   - 手机号/账号：你的认证账号；
   - 密码：你的登录密码；
   - 勾选“登录 Windows 后自动启动”；
   - 点击“保存”。
4. 保存后程序缩到右下角托盘，图标是一个蓝色圆角方块 + 白色对勾。
5. 双击托盘图标可以再次打开设置；右键托盘图标可以看到菜单。

> 如果首次保存时提示密码错误，程序仍会保存账号并继续在后台重试；建议先在“设置 → 测试登录”确认账号密码，或先用浏览器登录一次学校认证页面。

### 托盘菜单

| 菜单项 | 作用 |
| --- | --- |
| 状态：已连接 / 正在登录 / 登录失败 / 等待网络 | 显示当前状态 |
| 立即检查 / 登录 | 唤醒后台线程立即检查一次 |
| 设置... | 修改账号、密码、认证地址、开机自启 |
| 打开日志 | 打开日志目录 |
| 登录 Windows 后自动启动 | 勾选/取消注册表自启 |
| 退出 | 停止后台线程并退出 |

---

## 4. 开机自启说明

程序使用的是 **当前用户登录 Windows 后自启**：

```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
    CampusNetLogin = "<程序路径>" --tray
```

这意味着：

- 用户输入 Windows 密码、进入桌面后，程序自动启动并登录校园网。
- 不是“电脑开机、还没登录 Windows 就联网”。如果学校要求登录 Windows 之前就必须联网（例如域账户漫游、网络盘），需要改用“任务计划程序”并让任务在用户登录前运行；但那种方式通常拿不到当前用户的 DPAPI 密码，需要另外设计凭据保护方案，不能简单把密码明文放进计划任务。
- 如果移动了 exe 路径，自启命令会失效；重新打开设置保存一次即可刷新。
- 取消勾选“登录 Windows 后自动启动”会删除该注册表值。

### 用任务计划程序实现“开机即启动”（可选，进阶）

仅建议在“电脑固定、单人使用、允许后台运行”的场景使用，并且要接受凭据保护方式的变化：

1. 打开“任务计划程序” → “创建任务”。
2. 常规：勾选“不管用户是否登录都要运行”（需要管理员）。
3. 触发器：新建“启动时”。
4. 操作：启动程序，选择 `CampusNetLogin.exe`，参数 `--tray`。
5. 条件：取消“只有在使用交流电源时才启动”（笔记本按需）。
6. 设置：勾选“如果任务失败，按以下频率重新启动”。

注意：这种运行方式与当前用户的 DPAPI 配置不兼容，可能需要重新保存密码，或在“设置”里改用其他凭据保护方式。普通用户建议使用默认的“登录后自启”。

---

## 5. 配置文件、日志与诊断

| 内容 | 路径 |
| --- | --- |
| 配置 | `%LOCALAPPDATA%\CampusNetLogin\config.json` |
| 日志 | `%LOCALAPPDATA%\CampusNetLogin\logs\app.log` |
| 诊断报告 | `%LOCALAPPDATA%\CampusNetLogin\diagnose.txt` |
| 登录测试结果 | `%LOCALAPPDATA%\CampusNetLogin\login-test.txt` |

- 日志按天轮转，保留 30 天。
- 日志只记录状态、错误码和脱敏后的账号，不记录密码。
- 配置里的密码字段是 DPAPI 加密后的 Base64，不是明文；换 Windows 用户或换电脑后无法解密，需要重新输入。
- 删除配置即可恢复“首次运行”状态。

### 生成诊断报告

在 cmd 或 PowerShell 中运行：

```powershell
& "C:\路径\CampusNetLogin.exe" --diagnose
```

然后打开 `%LOCALAPPDATA%\CampusNetLogin\diagnose.txt`。报告包含：

- Windows 版本、程序路径；
- 当前认证地址、脱敏账号、是否已保存密码、是否开启自启；
- 当前网络是否在线、是否就绪；
- 门户标题、引擎版本、API 版本、公钥长度；
- 设备 ID、注册状态、IP、MAC；
- RSA 加密自检结果；
- 最近日志。

发给网络管理员时，请先检查报告里是否包含不希望外发的 IP/MAC；账号已脱敏，密码不会出现。

---

## 6. 命令行参数

```text
CampusNetLogin.exe                     启动托盘程序
CampusNetLogin.exe --setup             打开设置（已有配置也可打开）
CampusNetLogin.exe --diagnose          生成诊断报告，不弹窗
CampusNetLogin.exe --test-login        用已保存账号测试登录，弹窗显示结果
CampusNetLogin.exe --test-login --quiet 测试登录，不弹窗，只写文件
CampusNetLogin.exe --install-startup   设置登录 Windows 后自启
CampusNetLogin.exe --remove-startup    取消自启
CampusNetLogin.exe --version           显示版本
CampusNetLogin.exe --tray-test         内部自检：创建托盘 2.5 秒后退出，写 tray-test.txt
```

示例：

```powershell
$exe = "$env:LOCALAPPDATA\Programs\CampusNetLogin\CampusNetLogin.exe"
& $exe --diagnose
& $exe --test-login --quiet
Get-Content "$env:LOCALAPPDATA\CampusNetLogin\login-test.txt"
```

> `--test-login` 会直接向门户提交一次认证请求，即使当前已经在线也可能产生一次新会话；日常不需要频繁运行。

---

## 7. 从源码运行 / 重新打包

### 环境

- Python 3.9+（本机验证使用 3.12.9）。
- Windows 10/11。
- 依赖：`requests`、`pystray`、`Pillow`。
- 打包额外需要：`pyinstaller`。

```powershell
cd campus-net-login-py
python -m pip install -r requirements.txt
python main.py
```

源码运行首次会弹设置窗口；也可以：

```powershell
python main.py --diagnose
python main.py --test-login --quiet
```

### 打包单文件 exe

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成：

```
dist\CampusNetLogin.exe
```

打包脚本会：

- 使用 `--onefile --windowed`，终端电脑不需要 Python；
- 嵌入 `assets\app.ico`；
- 收集 `pystray` 的 Windows 后端；
- 自动包含 Tkinter、requests、Pillow。

> 首次运行单文件 exe 时会先解压到临时目录，启动比源码慢 1～3 秒，这是正常现象。杀毒软件如果误报，请把 exe 加入信任/白名单，不要关闭整个杀毒软件。

### 源码结构

```
campus-net-login-py/
├─ main.py            入口、命令行、单实例、日志
├─ ui.py              Tkinter 设置窗口、托盘、主应用
├─ worker.py          后台检查/登录线程
├─ portal.py          认证门户 API、RSA 加密、Session-Num
├─ online.py          联网探测
├─ netutil.py         地址/HTML 工具
├─ config.py          DPAPI 配置、日志目录
├─ autostart.py       HKCU Run 自启
├─ diagnostics.py     诊断报告、测试登录
├─ build.ps1          PyInstaller 打包
├─ requirements.txt
├─ assets/app.ico
└─ tests/             协议、界面、保存、托盘自检脚本
```

---

## 8. 工作原理（简版）

程序只做浏览器正常登录会做的事情：

1. `GET /access/1.0/windows/json/server/info` 获取门户标题、引擎版本、RSA 公钥、账号/密码提示。
2. `POST /access/1.0/windows/json/device/infoprocess`，`gettype=ipgetmac`，获取本机 `DeviceID`、注册状态、IP、MAC。
3. 用门户公钥按 RSA PKCS#1 v1.5 加密密码；手机号只做 Base64。
4. `POST /access/1.0/windows/json/auth/index` 提交登录。
5. 每个请求带门户要求的 `Session-Num`（对路径、查询串和表单键值做规范化后 MD5）和 `Referer`。
6. 登录成功后轮询联网探测地址，确认网络真的恢复，再进入定时检查。

不涉及：

- 不破解、不抓取浏览器保存的密码；
- 不修改客户端配置，不伪造设备指纹；
- 不绕过实名认证、设备合规检查、二次验证；
- 不向学校以外的服务器发送账号密码。

### 为什么密码用 RSA 加密，仍然建议只在内网使用

门户本身可能是 HTTP，登录表单在内网可见；RSA 只保护密码字段，不保护整个页面。程序不额外上传密码，但无法改变学校门户的传输方式。请在学校网络内使用，不要把认证地址改成陌生地址。

---

## 9. 安全与隐私

- **密码保护**：Windows DPAPI `CryptProtectData`，作用域为当前 Windows 用户；同一台电脑的其他用户无法直接解密。
- **不记录密码**：日志、诊断报告、异常信息都不包含密码。
- **账号脱敏**：日志/诊断里显示为 `138****0000` 形式。
- **本地配置**：`config.json` 只保存在本机 `%LOCALAPPDATA%`。
- **网络请求**：只访问配置的认证门户和两个联网探测地址（常见公共 DNS 或首页）。
- **代理**：程序显式禁用系统代理，避免被公司/学校代理劫持；如果学校要求代理上网，需要在代码中调整。
- **代码签名**：当前 exe 未做代码签名，Windows 可能提示“未知发布者”；请从可信渠道获取，或自行用机构证书签名。

### 合规提醒

- 本工具是个人效率工具，不代表官方客户端。
- 如果学校/单位明确要求使用指定准入程序，请继续保留并遵守；本工具不能替代设备合规检查。
- 不要用他人的手机号/密码登录，不要批量分发带密码的配置文件。
- 网络管理员如需调整认证策略，应以官方通知为准。

---

## 10. 常见问题

### Q1：提示“当前网络已经在线，无需重复登录”

说明当前已经认证成功，程序不会重复提交。想验证账号密码，可以：

- 先断开网络，再点“测试登录”；或
- 用 `--test-login` 直接测试（即使在线也会提交一次）；或
- 打开浏览器访问任意外部网站，确认是否真的能上网。

### Q2：提示“手机号/账号不存在或尚未开通认证”

说明账号不是有效的认证账号，或单位尚未开通。请联系网络管理员确认，不要反复试错。

### Q3：提示“密码错误”

请确认用的是 **对应平台的登录密码**，不是操作系统密码、不是 WiFi 密码。可以先用浏览器打开认证页面手动登录一次，确认密码正确。

### Q4：提示“认证门户版本已更新”

程序会自动下载前端脚本并识别新版本号后重试。如果仍失败，门户可能改了接口，需要更新程序。

### Q5：托盘图标找不到

- 点任务栏右下角的 `^` 展开隐藏图标；
- 在 Windows 设置 → 个性化 → 任务栏 → “选择哪些图标显示在任务栏上”里打开；
- 如果程序启动失败，查看 `%LOCALAPPDATA%\CampusNetLogin\logs\app.log`；
- 运行 `--diagnose` 生成报告。

### Q6：双击 exe 没反应

- 可能已经在运行（单实例）：看托盘；
- 可能被杀毒软件拦截：查看安全中心保护历史；
- 用 `--diagnose` 在 PowerShell 里运行，看是否有输出/报告；
- 单文件 exe 首次启动较慢，等 3～5 秒。

### Q7：换了电脑/换了 Windows 用户，密码失效

DPAPI 是按用户加密的，换用户/换电脑无法解密。打开设置重新输入密码并保存即可。

### Q8：如何完全卸载

1. 托盘右键 → 退出；
2. 设置里取消“登录 Windows 后自动启动”（或运行 `--remove-startup`）；
3. 删除 exe；
4. 删除 `%LOCALAPPDATA%\CampusNetLogin`（配置、日志、诊断报告）。

---

## 11. 已知限制

- 不同学校/单位、不同版本的门户字段可能不同；本程序针对主流认证门户实现。
- 如果门户启用图形验证码、短信二次验证、设备绑定审批，程序无法自动完成，需要在浏览器中处理。
- 如果单位强制使用特定客户端做设备合规检查，本程序不能替代它。
- 如果门户是 HTTP，局域网内仍存在流量可见风险；这是门户本身的限制。
- 单文件 exe 未签名，可能触发 SmartScreen/杀毒误报。

---

## 12. 测试记录（通用环境）

- 门户：典型教育城域网或高校认证门户。
- `server/info` 获取公钥成功。
- `device/infoprocess` 获取设备 ID，注册状态正常。
- RSA 自检通过。
- 虚拟账号登录：HTTP 请求成功，返回标准错误码，中文错误信息正常。
- 设置保存/DPAPI 解密/托盘创建/首次运行流程：本机自检通过。
- 打包 exe：`--diagnose`、`--test-login --quiet`、`--tray-test` 均通过。

---

## 13. 免责声明

本程序按“现状”提供，用于方便用户连接单位官方内网。使用前请确认符合网络管理规定；因账号保管不当、把程序用于未授权网络、或门户策略变更造成的后果，由使用者自行承担。
