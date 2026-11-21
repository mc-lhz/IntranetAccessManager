using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CampusNetLogin
{
    internal static class HttpUtil
    {
        public const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

        public static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Accept = "application/json, text/plain, */*";
            request.Proxy = null;
            request.KeepAlive = false;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }

        public static string ReadText(HttpWebResponse response, int maxBytes)
        {
            if (response == null)
            {
                return "";
            }
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    return "";
                }
                using (MemoryStream memory = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int total = 0;
                    while (total < maxBytes)
                    {
                        int read = stream.Read(buffer, 0, Math.Min(buffer.Length, maxBytes - total));
                        if (read <= 0)
                        {
                            break;
                        }
                        memory.Write(buffer, 0, read);
                        total += read;
                    }
                    byte[] data = memory.ToArray();
                    if (data.Length == 0)
                    {
                        return "";
                    }

                    string charset = response.CharacterSet;
                    Encoding encoding = Encoding.UTF8;
                    if (!string.IsNullOrWhiteSpace(charset))
                    {
                        try
                        {
                            encoding = Encoding.GetEncoding(charset);
                        }
                        catch
                        {
                        }
                    }
                    return encoding.GetString(data);
                }
            }
        }

        public static string GetOrigin(string url)
        {
            try
            {
                Uri uri = new Uri(url, UriKind.Absolute);
                return uri.GetLeftPart(UriPartial.Authority);
            }
            catch
            {
                return null;
            }
        }

        public static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "http://10.110.125.20";
            }
            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return "http://10.110.125.20";
            }
            return uri.GetLeftPart(UriPartial.Authority);
        }

        public static string FindPortalUrl(string text, string baseUrl)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            Match meta = Regex.Match(
                text,
                @"<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]+content\s*=\s*[""']?[^;]*;\s*url\s*=\s*([^""'>\s]+)",
                RegexOptions.IgnoreCase);
            if (meta.Success)
            {
                return CombineUrl(baseUrl, meta.Groups[1].Value.Trim('"', '\''));
            }

            Match js = Regex.Match(
                text,
                @"(?:window\.)?location(?:\.href)?\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (js.Success)
            {
                return CombineUrl(baseUrl, js.Groups[1].Value);
            }

            return null;
        }

        private static string CombineUrl(string baseUrl, string value)
        {
            try
            {
                Uri baseUri = new Uri(baseUrl);
                Uri combined = new Uri(baseUri, value);
                return combined.GetLeftPart(UriPartial.Authority);
            }
            catch
            {
                return null;
            }
        }
    }
}