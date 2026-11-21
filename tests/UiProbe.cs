using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CampusNetLogin;

internal static class UiProbe
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        StringBuilder sb = new StringBuilder();
        SettingsForm form = new SettingsForm(new AppConfig(), true);
        form.Show();
        Application.DoEvents();
        Dump(form, 0, sb);
        form.Close();
        string path = Path.Combine(AppConfig.DataDir, "ui-probe.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(path);
    }

    private static void Dump(Control control, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        sb.AppendLine(indent + control.GetType().Name
            + " bounds=" + control.Bounds
            + " text=\"" + (control.Text ?? "").Replace("\r", " ").Replace("\n", " ") + "\"");
        foreach (Control child in control.Controls)
        {
            Dump(child, depth + 1, sb);
        }
    }
}