using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace CampusNetLogin
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
                    | SecurityProtocolType.Tls11
                    | SecurityProtocolType.Tls;
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log.Initialize();

            bool diagnose = HasArg(args, "--diagnose");
            bool testLogin = HasArg(args, "--test-login");
            bool setup = HasArg(args, "--setup");
            bool installStartup = HasArg(args, "--install-startup");
            bool removeStartup = HasArg(args, "--remove-startup");
            bool help = HasArg(args, "--help") || HasArg(args, "/?");

            if (help)
            {
                MessageBox.Show(
                    "校园网自动登录\n\n"
                    + "直接运行：启动托盘程序\n"
                    + "--setup：打开设置\n"
                    + "--diagnose：生成诊断报告\n"
                    + "--test-login：用已保存账号测试登录\n"
                    + "--install-startup：设置登录时自动启动\n"
                    + "--remove-startup：取消自动启动",
                    "校园网自动登录",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (installStartup || removeStartup)
            {
                try
                {
                    AutoStart.SetEnabled(installStartup);
                    MessageBox.Show(installStartup ? "已设置登录 Windows 后自动启动。" : "已取消自动启动。",
                        "校园网自动登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("操作失败：" + ex.Message, "校园网自动登录",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (diagnose)
            {
                string path = Diagnostics.Run();
                Log.Info("诊断报告: " + path);
                return;
            }

            if (testLogin)
            {
                RunTestLogin();
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\CampusNetLogin_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("校园网自动登录已经在运行，请在任务栏右下角的托盘图标中操作。",
                        "校园网自动登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    AppConfig config = AppConfig.Load();
                    bool firstRun = !config.HasCredentials || !string.IsNullOrEmpty(config.PasswordError);

                    if (setup || firstRun)
                    {
                        using (SettingsForm form = new SettingsForm(config, firstRun))
                        {
                            if (form.ShowDialog() != DialogResult.OK)
                            {
                                return;
                            }
                            config = form.Config;
                        }
                    }

                    if (config.AutoStart)
                    {
                        try
                        {
                            AutoStart.SetEnabled(true);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("启动时设置自动启动失败: " + ex.Message);
                        }
                    }

                    using (TrayApplicationContext context = new TrayApplicationContext(config))
                    {
                        Application.Run(context);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("程序启动失败", ex);
                    MessageBox.Show("程序启动失败：" + ex.Message + "\n\n日志：" + Log.CurrentLogPath,
                        "校园网自动登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void RunTestLogin()
        {
            AppConfig config = AppConfig.Load();
            if (!config.HasCredentials)
            {
                MessageBox.Show("尚未保存账号和密码，请先运行设置。", "校园网自动登录",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                PortalClient client = new PortalClient(config);
                LoginResult result = client.Login(config.Username, config.Password);
                string path = Path.Combine(AppConfig.DataDir, "login-test.txt");
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine
                    + "账号: " + LoginService.MaskUsername(config.Username)
                    + Environment.NewLine
                    + "成功: " + (result.Success ? "是" : "否")
                    + Environment.NewLine
                    + "信息: " + result.Message
                    + Environment.NewLine
                    + "错误码: " + result.Code
                    + Environment.NewLine;
                File.WriteAllText(path, text);
                MessageBox.Show(text + Environment.NewLine + "结果已保存到：" + path,
                    "校园网自动登录", MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Exception("测试登录失败", ex);
                MessageBox.Show("测试登录失败：" + ex.Message, "校园网自动登录",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool HasArg(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}