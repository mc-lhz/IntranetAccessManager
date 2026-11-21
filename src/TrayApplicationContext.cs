using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CampusNetLogin
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly LoginService _service;
        private readonly Control _invoker = new Control();
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _loginItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private AppConfig _config;
        private bool _settingsOpen;

        public TrayApplicationContext(AppConfig config)
        {
            _config = config.Clone();

            // 用于把后台线程的状态安全地切回 UI 线程。
            IntPtr handle = _invoker.Handle;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _statusItem = new ToolStripMenuItem("正在启动...");
            _statusItem.Enabled = false;
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());

            _loginItem = new ToolStripMenuItem("立即检查 / 登录");
            _loginItem.Click += OnLoginClick;
            menu.Items.Add(_loginItem);

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("设置...");
            settingsItem.Click += OnSettingsClick;
            menu.Items.Add(settingsItem);

            ToolStripMenuItem logsItem = new ToolStripMenuItem("打开日志");
            logsItem.Click += OnLogsClick;
            menu.Items.Add(logsItem);

            _autoStartItem = new ToolStripMenuItem("登录 Windows 后自动启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = AutoStart.IsEnabled;
            _autoStartItem.Click += OnAutoStartClick;
            menu.Items.Add(_autoStartItem);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += OnExitClick;
            menu.Items.Add(exitItem);

            Icon icon = null;
            try
            {
                icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
            if (icon == null)
            {
                icon = SystemIcons.Application;
            }

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = icon;
            _notifyIcon.Text = "校园网自动登录";
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += OnSettingsClick;

            _service = new LoginService(_config);
            _service.StatusChanged += OnStatusChanged;
            _service.Notification += OnNotification;
            _service.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _service.Stop();
                }
                catch
                {
                }
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnStatusChanged(object sender, LoginStatusEventArgs e)
        {
            try
            {
                _invoker.BeginInvoke(new Action(delegate
                {
                    UpdateStatus(e.State, e.Message);
                }));
            }
            catch
            {
            }
        }

        private void UpdateStatus(LoginState state, string message)
        {
            string shortState;
            switch (state)
            {
                case LoginState.Online:
                    shortState = "已连接";
                    _loginItem.Text = "立即检查网络";
                    break;
                case LoginState.LoggingIn:
                    shortState = "正在登录";
                    _loginItem.Text = "正在登录...";
                    break;
                case LoginState.Error:
                    shortState = "登录失败";
                    _loginItem.Text = "立即重试登录";
                    break;
                case LoginState.WaitingNetwork:
                    shortState = "等待网络";
                    _loginItem.Text = "立即检查 / 登录";
                    break;
                case LoginState.NeedsSetup:
                    shortState = "需要设置";
                    _loginItem.Text = "立即检查 / 登录";
                    break;
                default:
                    shortState = "启动中";
                    _loginItem.Text = "立即检查 / 登录";
                    break;
            }

            _statusItem.Text = "状态：" + shortState + "  " + message;
            _loginItem.Enabled = state != LoginState.LoggingIn;
            string text = "校园网自动登录 - " + shortState;
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }
            _notifyIcon.Text = text;
        }

        private void OnNotification(object sender, string message)
        {
            try
            {
                _invoker.BeginInvoke(new Action(delegate
                {
                    ShowBalloon(message);
                }));
            }
            catch
            {
            }
        }

        private void ShowBalloon(string message)
        {
            if (!_notifyIcon.Visible)
            {
                return;
            }
            _notifyIcon.BalloonTipTitle = "校园网自动登录";
            _notifyIcon.BalloonTipText = message ?? "";
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4000);
        }

        private void OnLoginClick(object sender, EventArgs e)
        {
            _service.TriggerLogin();
            ShowBalloon("正在检查网络状态...");
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            try
            {
                using (SettingsForm form = new SettingsForm(_config, false))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        _config = form.Config;
                        _service.UpdateConfig(_config);
                        _autoStartItem.Checked = AutoStart.IsEnabled;
                        ShowBalloon("设置已保存，正在应用新配置。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("打开设置窗口失败", ex);
                MessageBox.Show("打开设置窗口失败：" + ex.Message, "校园网自动登录",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _settingsOpen = false;
            }
        }

        private void OnLogsClick(object sender, EventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", AppConfig.LogDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开日志目录失败：" + ex.Message, "校园网自动登录",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            try
            {
                AutoStart.SetEnabled(_autoStartItem.Checked);
                _config.AutoStart = _autoStartItem.Checked;
                _config.Save();
            }
            catch (Exception ex)
            {
                _autoStartItem.Checked = !_autoStartItem.Checked;
                MessageBox.Show("设置自动启动失败：" + ex.Message, "校园网自动登录",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _service.Stop();
            ExitThread();
        }
    }
}