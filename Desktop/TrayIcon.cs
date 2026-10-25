#if WINDOWS
using System.Drawing;
using System.Windows.Forms;

namespace LogLens.Desktop;

/// <summary>
/// System tray icon that keeps LogLens running in the background (no console window)
/// and gives the user a way to reopen the UI or exit the app.
/// </summary>
public static class TrayIcon
{
    public static void Start(IHostApplicationLifetime lifetime, string url, Action<string> openBrowser)
    {
        var thread = new Thread(() => Run(lifetime, url, openBrowser)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void ShowError(string message) =>
        MessageBox.Show(message, "LogLens", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static void Run(IHostApplicationLifetime lifetime, string url, Action<string> openBrowser)
    {
        using var menu = new ContextMenuStrip();
        using var icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "LogLens",
            ContextMenuStrip = menu,
            Visible = true
        };

        menu.Items.Add("Open LogLens", null, (_, _) => openBrowser(url));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            icon.Visible = false;
            lifetime.StopApplication();
            Application.ExitThread();
        });
        icon.DoubleClick += (_, _) => openBrowser(url);

        icon.ShowBalloonTip(4000, "LogLens is running",
            "Open it any time from this tray icon. Right-click it and choose Exit to close LogLens.",
            ToolTipIcon.Info);

        Application.Run();
    }
}
#endif
