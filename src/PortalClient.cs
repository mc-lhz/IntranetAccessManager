using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CampusNetLogin
{
    public class PortalException : Exception
    {
        public string Code { get; private set; }

        public PortalException(string message, string code)
            : base(message)
        {
            Code = code ?? "";
        }
    }

    public class ServerSnapshot
    {
        public string PortalUrl { get; set; }
        public string Engine { get; set; }
        public string ApiVersion { get; set; }
        public string PubKey { get; set; }
        public string Title { get; set; }
        public string ClientUserHint { get; set; }
        public string ClientPasswordHint { get; set; }
        public string AuthServer { get; set; }
        public bool ClientSavePass { get; set; }
        public bool NeedAuth { get; set; }
        public string RawError { get; set; }
    }

    public class DeviceSnapshot
    {
        public string DeviceId { get; set; }
        public bool Registered { get; set; }
        public string CheckResult { get; set; }
        public string IP { get; set; }
        public string Mac { get; set; }
        public string AscId { get; set; }
        public string DevName { get; set; }
        public string OSName { get; set; }
        public string RawError { get; set; }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public bool NeedsTwoFactor { get; set; }
        public string Message { get; set; }
        public string Code { get; set; }
        public string Token { get; set; }
        public string UserName { get; set; }

        public static LoginResult Ok(string token, string userName)
        {
            LoginResult r = new LoginResult();
            r.Success = true;
            r.Message = "登录成功";
            r.Token = token ?? "";
            r.UserName = userName ?? "";
            return r;
        }

        public static LoginResult Fail(string message, string code)
        {
            LoginResult r = new LoginResult();
            r.Success = false;
            r.Message = message;
            r.Code = code ?? "";
            return r;
        }
    }

    /// <summary>
    /// 盈高（Infogo）入网规范管理平台 / 深澜风格门户的登录客户端。
    /// 只复现浏览器正常登录时提交的请求，不绕过任何认证或设备检查。
    /// </summary>
    public class PortalClient
    {
        private const string ApiRoot = "/access/1.0/windows/json/";
        private const string UiPath = "/access/ui/index.html";

        private readonly AppConfig _config;
        private readonly CookieContainer _cookies = new CookieContainer();
        private readonly string _baseUrl;
        private string _apiVersion;
        private string _ascId;
        private ServerSnapshot _server;
        private DeviceSnapshot _device;
        private DateTime _serverTime = DateTime.MinValue;
        private DateTime _deviceTime = DateTime.MinValue;

        public string BaseUrl
        {
            get { return _baseUrl; }
        }

        public string ApiVersion
        {
            get { return _apiVersion; }
        }

        public PortalClient(AppConfig config)
        {
            _config = config.Clone();
            _baseUrl = HttpUtil.NormalizeBaseUrl(_config.PortalUrl);
            _apiVersion = string.IsNullOrWhiteSpace(_config.ApiVersion) ? "6039.3746" : _config.ApiVersion.Trim();
            _ascId = (_config.AscId ?? "").Trim();
        }

        public ServerSnapshot GetServerSnapshot(bool refresh)
        {
            if (refresh || _server == null || (DateTime.Now - _serverTime).TotalMinutes > 5)
            {
                _server = FetchServerInfo();
                _serverTime = DateTime.Now;
            }
            return _server;
        }

        public DeviceSnapshot GetDeviceSnapshot(bool refresh)
        {
            if (refresh || _device == null || (DateTime.Now - _deviceTime).TotalMinutes > 5)
            {
                _device = FetchDeviceInfo();
                _deviceTime = DateTime.Now;
            }
            return _device;
        }

        public LoginResult Login(string username, string password)
        {
            try
            {
                return LoginInternal(username, password, true);
            }
            catch (PortalException ex)
            {
                Log.Warn("登录失败: " + ex.Message + " (" + ex.Code + ")");
                return LoginResult.Fail(ex.Message, ex.Code);
            }
            catch (Exception ex)
            {
                Log.Exception("登录异常", ex);
                return LoginResult.Fail("登录异常: " + ex.Message, "EXCEPTION");
            }
        }

        private LoginResult LoginInternal(string username, string password, bool allowVersionRetry)
        {
            ServerSnapshot server = GetServerSnapshot(false);
            if (string.IsNullOrEmpty(server.PubKey))
            {
                throw new PortalException("认证服务器未返回加密公钥，请检查认证地址。", "NOPUBKEY");
            }

            DeviceSnapshot device = GetDeviceSnapshot(false);
            if (string.IsNullOrEmpty(device.DeviceId))
            {
                throw new PortalException("未能获取本机设备标识，请确认已连接校园网。", "NODEVICE");
            }

            string encryptedPassword = RsaEncrypt(password, server.PubKey);
            string userName64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(username ?? ""));

            List<KeyValuePair<string, string>> body = new List<KeyValuePair<string, string>>();
            body.Add(new KeyValuePair<string, string>("user_name", userName64));
            body.Add(new KeyValuePair<string, string>("deviceid", device.DeviceId));
            body.Add(new KeyValuePair<string, string>("idp", ""));
            body.Add(new KeyValuePair<string, string>("tokenId", ""));
            body.Add(new KeyValuePair<string, string>("remoteIp", ""));
            body.Add(new KeyValuePair<string, string>("password", encryptedPassword));
            body.Add(new KeyValuePair<string, string>("hintOver", "0"));
            body.Add(new KeyValuePair<string, string>("autoAuth", "0"));
            body.Add(new KeyValuePair<string, string>("verify_code", ""));
            body.Add(new KeyValuePair<string, string>("type", "User"));

            string query = "?local_lguage_set=zh&cache=" + NowMilliseconds();
            JsonObject root;
            try
            {
                root = PostJson("auth/index", query, body);
            }
            catch (PortalException ex)
            {
                if (allowVersionRetry && ex.Code == "21101015" && RefreshApiVersion())
                {
                    return LoginInternal(username, password, false);
                }
                throw;
            }

            int code = root.Int("errcode");
            if (code == 0)
            {
                JsonObject data = root.Object("data");
                if (data != null && data.Bool("FactorAuth"))
                {
                    LoginResult factor = LoginResult.Fail(
                        "该账号启用了二次验证，请打开认证网页完成短信/二次验证。",
                        "FactorAuth");
                    factor.NeedsTwoFactor = true;
                    return factor;
                }
                return LoginResult.Ok(
                    data == null ? "" : data.String("Token"),
                    data == null ? "" : data.String("UserName"));
            }

            if (code == 21101015 && allowVersionRetry && RefreshApiVersion())
            {
                return LoginInternal(username, password, false);
            }

            string message = FriendlyMessage(code, root.String("errmsg"));
            return LoginResult.Fail(message, code.ToString(CultureInfo.InvariantCulture));
        }

        private ServerSnapshot FetchServerInfo()
        {
            string query = "?basIP=&ascid=" + _ascId
                + "&firsturl=&version=" + _apiVersion
                + "&agentPromotion=&local_lguage_set=zh&cache=" + NowMilliseconds();
            JsonObject root = GetJson("server/info", query);
            int code = root.Int("errcode");
            if (code == 21101015 && RefreshApiVersion())
            {
                query = "?basIP=&ascid=" + _ascId
                    + "&firsturl=&version=" + _apiVersion
                    + "&agentPromotion=&local_lguage_set=zh&cache=" + NowMilliseconds();
                root = GetJson("server/info", query);
                code = root.Int("errcode");
            }
            if (code != 0)
            {
                throw new PortalException(FriendlyMessage(code, root.String("errmsg")), code.ToString(CultureInfo.InvariantCulture));
            }

            JsonObject data = root.Object("data");
            if (data == null)
            {
                throw new PortalException("认证服务器返回的数据不完整。", "NODATA");
            }

            ServerSnapshot snapshot = new ServerSnapshot();
            snapshot.PortalUrl = _baseUrl;
            snapshot.Engine = data.String("Engine");
            snapshot.ApiVersion = _apiVersion;
            snapshot.PubKey = data.String("pubKey");
            snapshot.RawError = "";

            JsonObject admin = data.Object("ADMINISTRATORINFO");
            if (admin != null)
            {
                snapshot.Title = admin.String("sTitle");
                if (string.IsNullOrEmpty(snapshot.Title))
                {
                    snapshot.Title = admin.String("indextitle");
                }
            }

            JsonObject clientAuth = data.Object("ClientAuth");
            if (clientAuth != null)
            {
                snapshot.ClientUserHint = clientAuth.String("ClientUser");
                snapshot.ClientPasswordHint = clientAuth.String("ClientPassword");
            }

            JsonObject user = data.Object("User");
            if (user != null)
            {
                snapshot.AuthServer = user.String("AuthServer");
            }

            JsonObject clientCheck = data.Object("CLIENTCHECK");
            if (clientCheck != null)
            {
                snapshot.ClientSavePass = clientCheck.Bool("ClientSavePass");
                snapshot.NeedAuth = clientCheck.Bool("IsAuth");
            }

            return snapshot;
        }

        private DeviceSnapshot FetchDeviceInfo()
        {
            List<KeyValuePair<string, string>> body = new List<KeyValuePair<string, string>>();
            body.Add(new KeyValuePair<string, string>("gettype", "ipgetmac"));
            body.Add(new KeyValuePair<string, string>("deviceid", ""));
            body.Add(new KeyValuePair<string, string>("license", ""));
            body.Add(new KeyValuePair<string, string>("UrlAswid", ""));
            body.Add(new KeyValuePair<string, string>("BrowserFingerprint", ""));
            body.Add(new KeyValuePair<string, string>("forbid", ""));
            body.Add(new KeyValuePair<string, string>("basIP", ""));
            body.Add(new KeyValuePair<string, string>("firstUrl", ""));
            body.Add(new KeyValuePair<string, string>("Mac", ""));
            body.Add(new KeyValuePair<string, string>("is_guest", "0"));
            body.Add(new KeyValuePair<string, string>("is_mobile", "0"));
            body.Add(new KeyValuePair<string, string>("os_platform", "Windows 10"));
            body.Add(new KeyValuePair<string, string>("browser", "Chrome/142.0.0.0"));
            body.Add(new KeyValuePair<string, string>("UrlAscid", _ascId));
            body.Add(new KeyValuePair<string, string>("wlanusermac", ""));
            body.Add(new KeyValuePair<string, string>("doorFrom", ""));
            body.Add(new KeyValuePair<string, string>("doorIP", ""));
            body.Add(new KeyValuePair<string, string>("doorMAC", ""));

            string query = "?local_lguage_set=zh&cache=" + NowMilliseconds();
            JsonObject root = PostJson("device/infoprocess", query, body);
            int code = root.Int("errcode");
            if (code != 0)
            {
                throw new PortalException(FriendlyMessage(code, root.String("errmsg")), code.ToString(CultureInfo.InvariantCulture));
            }

            JsonObject data = root.Object("data");
            if (data == null)
            {
                throw new PortalException("认证服务器未返回设备信息。", "NODEVICE");
            }

            DeviceSnapshot snapshot = new DeviceSnapshot();
            snapshot.DeviceId = data.String("DeviceID");
            snapshot.Registered = data.Bool("Registered");
            snapshot.CheckResult = data.String("CheckResult");
            snapshot.IP = data.String("IP");
            snapshot.Mac = data.String("Mac");
            snapshot.AscId = data.String("AscID");
            snapshot.DevName = data.String("DevName");
            snapshot.OSName = data.String("OSName");
            snapshot.RawError = "";

            if (string.IsNullOrEmpty(snapshot.DeviceId))
            {
                throw new PortalException("认证服务器返回的设备标识为空，请稍后重试。", "NODEVICE");
            }
            return snapshot;
        }

        private JsonObject GetJson(string apiPath, string query)
        {
            string pathAndQuery = ApiRoot + apiPath + query;
            HttpWebRequest request = HttpUtil.CreateRequest(_baseUrl + pathAndQuery);
            request.Method = "GET";
            request.Referer = _baseUrl + UiPath;
            request.Headers["Session-Num"] = ComputeSessionNum(pathAndQuery, null);
            request.Headers["X-Requested-With"] = "XMLHttpRequest";
            request.Headers["Accept-Language"] = "zh-CN,zh;q=0.9";
            request.CookieContainer = _cookies;
            return Execute(request);
        }

        private JsonObject PostJson(string apiPath, string query, IList<KeyValuePair<string, string>> body)
        {
            string pathAndQuery = ApiRoot + apiPath + query;
            string encoded = EncodeForm(body);
            byte[] bytes = Encoding.UTF8.GetBytes(encoded);

            HttpWebRequest request = HttpUtil.CreateRequest(_baseUrl + pathAndQuery);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
            request.Referer = _baseUrl + UiPath;
            request.Headers["Session-Num"] = ComputeSessionNum(pathAndQuery, body);
            request.Headers["X-Requested-With"] = "XMLHttpRequest";
            request.Headers["Accept-Language"] = "zh-CN,zh;q=0.9";
            request.CookieContainer = _cookies;
            request.ContentLength = bytes.Length;

            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            return Execute(request);
        }

        private JsonObject Execute(HttpWebRequest request)
        {
            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    throw new PortalException("无法连接认证服务器：" + ex.Message, "NETWORK");
                }
            }

            using (response)
            {
                string text = HttpUtil.ReadText(response, 1024 * 1024);
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new PortalException("认证服务器返回空响应（HTTP " + (int)response.StatusCode + "）。", "EMPTY");
                }

                JsonObject root;
                try
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    object raw = serializer.DeserializeObject(text);
                    root = JsonObject.Wrap(raw);
                }
                catch (Exception ex)
                {
                    Log.Warn("认证服务器返回非 JSON 内容: " + text.Substring(0, Math.Min(200, text.Length)));
                    throw new PortalException("认证服务器返回格式异常：" + ex.Message, "BADJSON");
                }

                if (root == null)
                {
                    throw new PortalException("认证服务器返回格式异常。", "BADJSON");
                }
                return root;
            }
        }

        private bool RefreshApiVersion()
        {
            try
            {
                string url = _baseUrl + "/access/ui/js/index.js";
                HttpWebRequest request = HttpUtil.CreateRequest(url);
                request.Method = "GET";
                request.Timeout = 25000;
                request.ReadWriteTimeout = 25000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    string js = HttpUtil.ReadText(response, 4 * 1024 * 1024);
                    Match match = Regex.Match(
                        js,
                        @"getGlobalServerDispose[\s\S]{0,1200}?version\s*=\s*""([0-9]{3,5}\.[0-9]{3,5})""",
                        RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        match = Regex.Match(js, @"version\s*=\s*""([0-9]{3,5}\.[0-9]{3,5})""");
                    }
                    if (match.Success)
                    {
                        string version = match.Groups[1].Value;
                        if (version != _apiVersion)
                        {
                            Log.Info("检测到认证门户版本更新: " + _apiVersion + " -> " + version);
                            _apiVersion = version;
                            _server = null;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("自动获取门户版本失败: " + ex.Message);
            }
            return false;
        }

        private static string FriendlyMessage(int code, string serverMessage)
        {
            switch (code)
            {
                case 21100006:
                    return "认证请求被拒绝（门户地址或版本不匹配，请检查认证地址）。";
                case 21101015:
                    return "认证门户版本已更新，请稍后重试。";
                case 21134001:
                    return "手机号不存在或尚未开通 I教育平台认证。";
                case 21134002:
                    return "密码错误，请重新输入。";
                case 21133003:
                case 21133015:
                case 21133017:
                    return "密码已过期，请先在认证网页修改密码。";
                case 21120030:
                    return "需要在认证网页完成设备或账号确认。";
                default:
                    if (!string.IsNullOrWhiteSpace(serverMessage))
                    {
                        return serverMessage;
                    }
                    return "登录失败（错误码 " + code.ToString(CultureInfo.InvariantCulture) + "）。";
            }
        }

        private static string EncodeForm(IList<KeyValuePair<string, string>> body)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < body.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('&');
                }
                sb.Append(Uri.EscapeDataString(body[i].Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(body[i].Value ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 门户的 CSRF 头：把 URL 和表单参数拼接后，过滤特殊字符，再取 MD5。
        /// 与浏览器端 addFixedParam / computeCsrfToke 的行为保持一致。
        /// </summary>
        private static string ComputeSessionNum(string pathAndQuery, IList<KeyValuePair<string, string>> body)
        {
            StringBuilder raw = new StringBuilder(pathAndQuery);
            if (body != null)
            {
                for (int i = 0; i < body.Count; i++)
                {
                    raw.Append('&');
                    raw.Append(body[i].Key);
                    raw.Append('=');
                    raw.Append(body[i].Value ?? "");
                }
            }

            string sanitized = Regex.Replace(raw.ToString(), @"[^0-9a-zA-Z=&()/._?'""<>]", "");
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sanitized));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return hex.ToString();
            }
        }

        private static long NowMilliseconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public static string RsaEncrypt(string plainText, string pem)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                throw new ArgumentException("密码不能为空。", "plainText");
            }
            if (string.IsNullOrWhiteSpace(pem))
            {
                throw new ArgumentException("公钥为空。", "pem");
            }

            bool pkcs1 = pem.IndexOf("RSA PUBLIC KEY", StringComparison.OrdinalIgnoreCase) >= 0;
            string base64 = Regex.Replace(pem, "-----[^-]+-----", "");
            base64 = Regex.Replace(base64, @"\s+", "");
            byte[] der = Convert.FromBase64String(base64);

            DerElement outer = ReadElement(der, 0);
            int pos = outer.Start;
            DerElement modulusElement;
            DerElement exponentElement;

            if (pkcs1)
            {
                modulusElement = ReadElement(der, pos);
                exponentElement = ReadElement(der, modulusElement.Next);
            }
            else
            {
                DerElement algorithm = ReadElement(der, pos);
                DerElement bitString = ReadElement(der, algorithm.Next);
                int keyPos = bitString.Start + 1;
                DerElement rsaSequence = ReadElement(der, keyPos);
                modulusElement = ReadElement(der, rsaSequence.Start);
                exponentElement = ReadElement(der, modulusElement.Next);
            }

            byte[] modulus = CopyAndTrim(modulusElement, der);
            byte[] exponent = CopyAndTrim(exponentElement, der);
            if (modulus.Length < 64 || exponent.Length == 0)
            {
                throw new PortalException("认证服务器公钥格式异常。", "BADKEY");
            }

            RSAParameters parameters = new RSAParameters();
            parameters.Modulus = modulus;
            parameters.Exponent = exponent;

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(parameters);
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                int max = (rsa.KeySize / 8) - 11;
                if (data.Length > max)
                {
                    throw new PortalException("密码过长，无法使用门户公钥加密。", "PASSWORDTOOLONG");
                }
                byte[] encrypted = rsa.Encrypt(data, false);
                return Convert.ToBase64String(encrypted);
            }
        }

        private static byte[] CopyAndTrim(DerElement element, byte[] data)
        {
            int start = element.Start;
            int length = element.Length;
            while (length > 1 && data[start] == 0)
            {
                start++;
                length--;
            }
            byte[] result = new byte[length];
            Array.Copy(data, start, result, 0, length);
            return result;
        }

        private class DerElement
        {
            public byte Tag;
            public int Start;
            public int Length;
            public int Next;
        }

        private static DerElement ReadElement(byte[] data, int position)
        {
            if (position < 0 || position + 2 > data.Length)
            {
                throw new PortalException("公钥数据不完整。", "BADKEY");
            }

            DerElement element = new DerElement();
            element.Tag = data[position];
            position++;
            int length = data[position];
            position++;
            if ((length & 0x80) != 0)
            {
                int count = length & 0x7f;
                if (count <= 0 || count > 4 || position + count > data.Length)
                {
                    throw new PortalException("公钥长度字段异常。", "BADKEY");
                }
                length = 0;
                for (int i = 0; i < count; i++)
                {
                    length = (length << 8) | data[position];
                    position++;
                }
            }

            if (length < 0 || position + length > data.Length)
            {
                throw new PortalException("公钥内容越界。", "BADKEY");
            }

            element.Start = position;
            element.Length = length;
            element.Next = position + length;
            return element;
        }
    }

    internal class JsonObject
    {
        private readonly Dictionary<string, object> _values;

        private JsonObject(Dictionary<string, object> values)
        {
            _values = values;
        }

        public static JsonObject Wrap(object value)
        {
            Dictionary<string, object> dict = value as Dictionary<string, object>;
            if (dict == null)
            {
                return null;
            }
            return new JsonObject(dict);
        }

        public bool Has(string key)
        {
            return _values.ContainsKey(key);
        }

        public string String(string key)
        {
            object value;
            if (!_values.TryGetValue(key, out value) || value == null)
            {
                return "";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public int Int(string key)
        {
            object value;
            if (!_values.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            int result;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result))
            {
                return result;
            }
            return 0;
        }

        public bool Bool(string key)
        {
            object value;
            if (!_values.TryGetValue(key, out value) || value == null)
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text == "1"
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }

        public JsonObject Object(string key)
        {
            object value;
            if (!_values.TryGetValue(key, out value))
            {
                return null;
            }
            return Wrap(value);
        }
    }
}