using System;
using System.Net;
using System.Net.NetworkInformation;

namespace CampusNetLogin
{
    public class OnlineCheckResult
    {
        public bool Online { get; set; }
        public bool NetworkReady { get; set; }
        public string PortalUrl { get; set; }
        public string Detail { get; set; }
    }

    public static class OnlineChecker
    {
        private static readonly string[] ProbeUrls =
        {
            "http://connect.rom.miui.com/generate_204",
            "http://www.baidu.com/"
        };

        public static OnlineCheckResult Check(string configuredPortal)
        {
            OnlineCheckResult result = new OnlineCheckResult();
            result.PortalUrl = HttpUtil.NormalizeBaseUrl(configuredPortal);
            result.NetworkReady = NetworkInterface.GetIsNetworkAvailable();

            string lastError = "";
            bool gotResponse = false;

            foreach (string url in ProbeUrls)
            {
                HttpWebResponse response = null;
                try
                {
                    HttpWebRequest request = HttpUtil.CreateRequest(url);
                    request.Method = "GET";
                    request.Timeout = 7000;
                    request.ReadWriteTimeout = 7000;
                    response = (HttpWebResponse)request.GetResponse();
                    gotResponse = true;
                    int status = (int)response.StatusCode;

                    if (status == 204)
                    {
                        result.Online = true;
                        result.NetworkReady = true;
                        result.Detail = "网络已连接 (" + url + " 返回 204)";
                        return result;
                    }

                    if (status >= 300 && status < 400)
                    {
                        string location = response.Headers["Location"];
                        string portal = HttpUtil.GetOrigin(location);
                        if (!string.IsNullOrEmpty(portal))
                        {
                            result.PortalUrl = portal;
                        }
                        result.Online = false;
                        result.NetworkReady = true;
                        result.Detail = "检测到入网重定向: " + (location ?? "(无 Location)");
                        return result;
                    }

                    if (status == 200)
                    {
                        string body = HttpUtil.ReadText(response, 65536);
                        if (IsPortalBody(body))
                        {
                            string portal = HttpUtil.FindPortalUrl(body, url);
                            if (!string.IsNullOrEmpty(portal))
                            {
                                result.PortalUrl = portal;
                            }
                            result.Online = false;
                            result.NetworkReady = true;
                            result.Detail = "检测到入网认证页面";
                            return result;
                        }

                        if (url.IndexOf("generate_204", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Online = string.IsNullOrEmpty(body);
                            result.NetworkReady = true;
                            result.Detail = result.Online ? "网络已连接 (204 探测通过)" : "探测页返回非空内容";
                            if (result.Online)
                            {
                                return result;
                            }
                        }
                        else if (body.IndexOf("baidu", StringComparison.OrdinalIgnoreCase) >= 0
                            || body.IndexOf("百度", StringComparison.Ordinal) >= 0)
                        {
                            result.Online = true;
                            result.NetworkReady = true;
                            result.Detail = "网络已连接 (百度探测通过)";
                            return result;
                        }
                    }
                }
                catch (WebException ex)
                {
                    response = ex.Response as HttpWebResponse;
                    if (response != null)
                    {
                        gotResponse = true;
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400)
                        {
                            string location = response.Headers["Location"];
                            string portal = HttpUtil.GetOrigin(location);
                            if (!string.IsNullOrEmpty(portal))
                            {
                                result.PortalUrl = portal;
                            }
                            result.Online = false;
                            result.NetworkReady = true;
                            result.Detail = "检测到入网重定向: " + (location ?? "(无 Location)");
                            return result;
                        }
                    }
                    lastError = ex.Status.ToString() + ": " + ex.Message;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    if (response != null)
                    {
                        response.Close();
                    }
                }
            }

            result.Online = false;
            result.NetworkReady = result.NetworkReady || gotResponse;
            result.Detail = gotResponse
                ? "网络未认证" + (string.IsNullOrEmpty(lastError) ? "" : " (" + lastError + ")")
                : "网络尚未就绪" + (string.IsNullOrEmpty(lastError) ? "" : " (" + lastError + ")");
            return result;
        }

        public static bool IsPortalBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }
            return body.IndexOf("入网规范管理平台", StringComparison.Ordinal) >= 0
                || body.IndexOf("access/ui", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("index_redirec", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("Infogo", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("srun_portal", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("深澜", StringComparison.Ordinal) >= 0;
        }
    }
}