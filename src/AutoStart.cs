using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CampusNetLogin
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CampusNetLogin";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        if (key == null)
                        {
                            return false;
                        }
                        string value = key.GetValue(ValueName) as string;
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string CurrentCommand
        {
            get
            {
                return "\"" + Application.ExecutablePath + "\" --tray";
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        return;
                    }
                    if (enabled)
                    {
                        key.SetValue(ValueName, CurrentCommand, RegistryValueKind.String);
                        Log.Info("已设置登录时自动启动: " + CurrentCommand);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                        Log.Info("已取消登录时自动启动");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("设置自动启动失败: " + ex.Message);
                throw;
            }
        }
    }
}