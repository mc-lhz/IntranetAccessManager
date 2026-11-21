using System;
using System.Globalization;
using System.Threading;

namespace CampusNetLogin
{
    public enum LoginState
    {
        Starting,
        Online,
        LoggingIn,
        Error,
        WaitingNetwork,
        NeedsSetup
    }

    public class LoginStatusEventArgs : EventArgs
    {
        public LoginState State { get; private set; }
        public string Message { get; private set; }

        public LoginStatusEventArgs(LoginState state, string message)
        {
            State = state;
            Message = message ?? "";
        }
    }

    public class LoginService : IDisposable
    {
        private readonly object _sync = new object();
        private AppConfig _config;
        private Thread _thread;
        private volatile bool _stopping;
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private LoginState _lastState;
        private string _lastMessage;
        private DateTime _lastNotification = DateTime.MinValue;

        public event EventHandler<LoginStatusEventArgs> StatusChanged;
        public event EventHandler<string> Notification;

        public LoginService(AppConfig config)
        {
            _config = config.Clone();
        }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _stopping = false;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "CampusNetLoginWorker";
            _thread.Start();
        }

        public void Stop()
        {
            _stopping = true;
            _wake.Set();
            Thread thread = _thread;
            if (thread != null && thread.IsAlive)
            {
                thread.Join(4000);
            }
            _thread = null;
        }

        public void UpdateConfig(AppConfig config)
        {
            lock (_sync)
            {
                _config = config.Clone();
            }
            _wake.Set();
        }

        public void TriggerLogin()
        {
            _wake.Set();
        }

        public void Dispose()
        {
            Stop();
            _wake.Close();
        }

        private AppConfig GetConfig()
        {
            lock (_sync)
            {
                return _config == null ? null : _config.Clone();
            }
        }

        private void Run()
        {
            Log.Info("登录服务已启动");
            while (!_stopping)
            {
                AppConfig config = GetConfig();
                if (config == null || !config.HasCredentials)
                {
                    SetState(LoginState.NeedsSetup, "请先填写手机号和密码");
                    Wait(30);
                    continue;
                }

                OnlineCheckResult check = OnlineChecker.Check(config.PortalUrl);
                if (_stopping)
                {
                    break;
                }

                if (check.Online)
                {
                    SetState(LoginState.Online, "网络已连接");
                    Wait(config.CheckIntervalSeconds);
                    continue;
                }

                if (!check.NetworkReady)
                {
                    SetState(LoginState.WaitingNetwork, "等待网络连接...");
                    Wait(10);
                    continue;
                }

                SetState(LoginState.LoggingIn, "正在登录校园网...");
                PortalClient client = new PortalClient(config);
                LoginResult result = client.Login(config.Username, config.Password);
                if (_stopping)
                {
                    break;
                }

                if (result.Success)
                {
                    bool online = WaitForOnline(12);
                    if (_stopping)
                    {
                        break;
                    }

                    if (online)
                    {
                        config.LastSuccess = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(client.ApiVersion)
                            && client.ApiVersion != config.ApiVersion)
                        {
                            config.ApiVersion = client.ApiVersion;
                        }
                        try
                        {
                            config.Save();
                        }
                        catch
                        {
                        }
                        SetState(LoginState.Online, "登录成功，网络已连接");
                        Notify("校园网已连接");
                        Wait(config.CheckIntervalSeconds);
                    }
                    else
                    {
                        SetState(LoginState.Error, "认证成功，但网络尚未恢复");
                        NotifyThrottled("认证成功但网络尚未恢复，请稍后重试。");
                        Wait(config.RetryIntervalSeconds);
                    }
                    continue;
                }

                SetState(LoginState.Error, result.Message);
                NotifyThrottled("登录失败：" + result.Message);
                Wait(config.RetryIntervalSeconds);
            }
            Log.Info("登录服务已停止");
        }

        private bool WaitForOnline(int seconds)
        {
            int end = Environment.TickCount + seconds * 1000;
            while (!_stopping && Environment.TickCount < end)
            {
                Thread.Sleep(2000);
                OnlineCheckResult check = OnlineChecker.Check(GetConfig().PortalUrl);
                if (check.Online)
                {
                    return true;
                }
            }
            return false;
        }

        private bool Wait(int seconds)
        {
            if (_stopping)
            {
                return false;
            }
            _wake.WaitOne(Math.Max(1, seconds) * 1000, false);
            _wake.Reset();
            return !_stopping;
        }

        private void SetState(LoginState state, string message)
        {
            if (_lastState == state && string.Equals(_lastMessage, message, StringComparison.Ordinal))
            {
                return;
            }
            _lastState = state;
            _lastMessage = message;
            Log.Info("状态: " + state + " - " + message);
            EventHandler<LoginStatusEventArgs> handler = StatusChanged;
            if (handler != null)
            {
                try
                {
                    handler(this, new LoginStatusEventArgs(state, message));
                }
                catch (Exception ex)
                {
                    Log.Warn("状态回调异常: " + ex.Message);
                }
            }
        }

        private void Notify(string message)
        {
            _lastNotification = DateTime.Now;
            EventHandler<string> handler = Notification;
            if (handler != null)
            {
                try
                {
                    handler(this, message);
                }
                catch
                {
                }
            }
        }

        private void NotifyThrottled(string message)
        {
            if (DateTime.Now - _lastNotification < TimeSpan.FromMinutes(10))
            {
                return;
            }
            Notify(message);
        }

        public static string MaskUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return "(未设置)";
            }
            if (username.Length <= 4)
            {
                return "****";
            }
            if (username.Length <= 8)
            {
                return username.Substring(0, 2) + "****";
            }
            return username.Substring(0, 3) + "****" + username.Substring(username.Length - 4);
        }
    }
}