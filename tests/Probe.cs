using System;
using System.IO;
using System.Text;
using CampusNetLogin;

internal static class Probe
{
    private static void Main()
    {
        AppConfig config = new AppConfig();
        config.PortalUrl = "http://10.110.125.20";
        config.Username = "13800000000";
        config.Password = "Test1234!";
        config.ApiVersion = "6039.3746";

        StringBuilder output = new StringBuilder();
        try
        {
            PortalClient client = new PortalClient(config);
            ServerSnapshot server = client.GetServerSnapshot(true);
            output.AppendLine("server=" + server.Engine + " pubkey=" + server.PubKey.Length);
            DeviceSnapshot device = client.GetDeviceSnapshot(true);
            output.AppendLine("device=" + device.DeviceId + " registered=" + device.Registered);
            LoginResult result = client.Login(config.Username, config.Password);
            output.AppendLine("success=" + result.Success);
            output.AppendLine("code=" + result.Code);
            output.AppendLine("message=" + result.Message);
        }
        catch (Exception ex)
        {
            output.AppendLine("EXCEPTION: " + ex);
        }

        string path = Path.Combine(AppConfig.DataDir, "probe.txt");
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
        Console.WriteLine(path);
    }
}