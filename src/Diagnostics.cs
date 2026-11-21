using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CampusNetLogin
{
    internal static class Diagnostics
    {
        public static string Run()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("校园网自动登录 - 诊断报告");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.AppendLine("系统: " + Environment.OSVersion);
            report.AppendLine("CLR: " + Environment.Version);
            report.AppendLine("程序版本: 1.0.0");
            report.AppendLine("配置: " + AppConfig.ConfigPath);
            report.AppendLine();

            AppConfig config = AppConfig.Load();
            report.AppendLine("认证地址: " + config.PortalUrl);
            report.AppendLine("账号: " + LoginService.MaskUsername(config.Username));
            report.AppendLine("已保存密码: " + (string.IsNullOrEmpty(config.Password) ? "否" : "是"));
            report.AppendLine("自动启动: " + (AutoStart.IsEnabled ? "是" : "否"));
            report.AppendLine();

            report.AppendLine("== 网络探测 ==");
            try
            {
                OnlineCheckResult online = OnlineChecker.Check(config.PortalUrl);
                report.AppendLine("在线: " + (online.Online ? "是" : "否"));
                report.AppendLine("网络就绪: " + (online.NetworkReady ? "是" : "否"));
                report.AppendLine("门户地址: " + (online.PortalUrl ?? ""));
                report.AppendLine("说明: " + (online.Detail ?? ""));
            }
            catch (Exception ex)
            {
                report.AppendLine("网络探测失败: " + ex.Message);
            }
            report.AppendLine();

            PortalClient client = new PortalClient(config);
            ServerSnapshot server = null;
            report.AppendLine("== 门户信息 ==");
            try
            {
                server = client.GetServerSnapshot(true);
                report.AppendLine("门户标题: " + (server.Title ?? ""));
                report.AppendLine("引擎版本: " + (server.Engine ?? ""));
                report.AppendLine("API 版本: " + (server.ApiVersion ?? ""));
                report.AppendLine("公钥长度: " + (server.PubKey == null ? 0 : server.PubKey.Length));
                report.AppendLine("认证类型: " + (server.AuthServer ?? ""));
                report.AppendLine("账号提示: " + (server.ClientUserHint ?? ""));
                report.AppendLine("密码提示: " + (server.ClientPasswordHint ?? ""));
            }
            catch (Exception ex)
            {
                report.AppendLine("读取门户信息失败: " + ex.Message);
            }
            report.AppendLine();

            report.AppendLine("== 设备信息 ==");
            try
            {
                DeviceSnapshot device = client.GetDeviceSnapshot(true);
                report.AppendLine("设备 ID: " + device.DeviceId);
                report.AppendLine("已注册: " + (device.Registered ? "是" : "否"));
                report.AppendLine("检查结果: " + (device.CheckResult ?? ""));
                report.AppendLine("IP: " + (device.IP ?? ""));
                report.AppendLine("MAC: " + (device.Mac ?? ""));
                report.AppendLine("AscID: " + (device.AscId ?? ""));
                report.AppendLine("设备名: " + (device.DevName ?? ""));
                report.AppendLine("系统: " + (device.OSName ?? ""));
            }
            catch (Exception ex)
            {
                report.AppendLine("读取设备信息失败: " + ex.Message);
            }
            report.AppendLine();

            report.AppendLine("== 加密自检 ==");
            try
            {
                if (server == null || string.IsNullOrEmpty(server.PubKey))
                {
                    report.AppendLine("跳过：未取得公钥");
                }
                else
                {
                    string encrypted = PortalClient.RsaEncrypt("diagnose-test", server.PubKey);
                    report.AppendLine("RSA 加密成功，密文 Base64 长度: " + encrypted.Length);
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("RSA 加密失败: " + ex.Message);
            }
            report.AppendLine();

            report.AppendLine("== 最近日志 ==");
            try
            {
                string logPath = Log.CurrentLogPath;
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                    int start = Math.Max(0, lines.Length - 20);
                    for (int i = start; i < lines.Length; i++)
                    {
                        report.AppendLine(lines[i]);
                    }
                }
                else
                {
                    report.AppendLine("(暂无日志)");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("读取日志失败: " + ex.Message);
            }

            string path = Path.Combine(AppConfig.DataDir, "diagnose.txt");
            try
            {
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("写入诊断报告失败: " + ex.Message);
            }
            Log.Info("诊断完成: " + path);
            return path;
        }
    }
}