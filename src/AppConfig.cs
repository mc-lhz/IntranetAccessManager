using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

namespace CampusNetLogin
{
    /// <summary>
    /// 本机配置。密码使用 Windows DPAPI（当前用户）加密，绝不保存明文。
    /// </summary>
    public class AppConfig
    {
        public string PortalUrl { get; set; }
        public string Username { get; set; }
        public string PasswordProtected { get; set; }
        public bool AutoStart { get; set; }
        public int CheckIntervalSeconds { get; set; }
        public int RetryIntervalSeconds { get; set; }
        public string ApiVersion { get; set; }
        public string AscId { get; set; }
        public string LastSuccess { get; set; }

        [ScriptIgnore]
        public string Password { get; set; }

        [ScriptIgnore]
        public string PasswordError { get; set; }

        public AppConfig()
        {
            PortalUrl = "http://10.110.125.20";
            Username = "";
            PasswordProtected = "";
            Password = "";
            AutoStart = true;
            CheckIntervalSeconds = 60;
            RetryIntervalSeconds = 20;
            ApiVersion = "6039.3746";
            AscId = "";
            LastSuccess = "";
        }

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CampusNetLogin");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataDir, "config.json"); }
        }

        public static string LogDir
        {
            get
            {
                string dir = Path.Combine(DataDir, "logs");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        public bool HasCredentials
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Username)
                    && !string.IsNullOrEmpty(Password);
            }
        }

        public AppConfig Clone()
        {
            AppConfig c = (AppConfig)this.MemberwiseClone();
            return c;
        }

        public void SetPassword(string password)
        {
            Password = password ?? "";
            if (string.IsNullOrEmpty(Password))
            {
                PasswordProtected = "";
                return;
            }

            byte[] plain = Encoding.UTF8.GetBytes(Password);
            byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            PasswordProtected = Convert.ToBase64String(cipher);
        }

        public bool TryUnprotectPassword()
        {
            PasswordError = null;
            if (!string.IsNullOrEmpty(Password))
            {
                return true;
            }
            if (string.IsNullOrEmpty(PasswordProtected))
            {
                Password = "";
                return false;
            }

            try
            {
                byte[] cipher = Convert.FromBase64String(PasswordProtected);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                Password = Encoding.UTF8.GetString(plain);
                return true;
            }
            catch (Exception ex)
            {
                Password = "";
                PasswordError = "无法解密本机保存的密码，请重新输入。";
                Log.Warn("DPAPI 解密失败: " + ex.Message);
                return false;
            }
        }

        public static AppConfig Load()
        {
            AppConfig config = new AppConfig();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    AppConfig loaded = serializer.Deserialize<AppConfig>(json);
                    if (loaded != null)
                    {
                        config = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败: " + ex.Message);
                try
                {
                    string backup = ConfigPath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    if (File.Exists(ConfigPath))
                    {
                        File.Copy(ConfigPath, backup, true);
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(config.PortalUrl))
            {
                config.PortalUrl = "http://10.110.125.20";
            }
            if (string.IsNullOrWhiteSpace(config.ApiVersion))
            {
                config.ApiVersion = "6039.3746";
            }
            if (config.CheckIntervalSeconds < 15)
            {
                config.CheckIntervalSeconds = 60;
            }
            if (config.RetryIntervalSeconds < 10)
            {
                config.RetryIntervalSeconds = 20;
            }

            config.TryUnprotectPassword();
            return config;
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(ConfigPath))
                {
                    try
                    {
                        File.Replace(tmp, ConfigPath, null);
                    }
                    catch
                    {
                        File.Copy(tmp, ConfigPath, true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, ConfigPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败: " + ex.Message);
                throw;
            }
        }
    }
}