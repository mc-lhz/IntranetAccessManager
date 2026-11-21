using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CampusNetLogin
{
    public static class Log
    {
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static string CurrentLogPath
        {
            get
            {
                string name = "app-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                return Path.Combine(AppConfig.LogDir, name);
            }
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            try
            {
                CleanupOldLogs();
            }
            catch
            {
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Exception(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                Initialize();
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.Append(" [");
                sb.Append(level);
                sb.Append("] ");
                sb.Append(message);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.ToString());
                }

                lock (Sync)
                {
                    File.AppendAllText(CurrentLogPath, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private static void CleanupOldLogs()
        {
            DateTime cutoff = DateTime.Now.AddDays(-30);
            foreach (string file in Directory.GetFiles(AppConfig.LogDir, "app-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
    }
}