using DataDonkTM.Core;
using DataDonkTM.UI;

namespace DataDonkTM;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\DataDonkTM.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("DataDonk Table Manager is already running (see the tray icon).", "DataDonk Table Manager");
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        ConfigStore.Load();
        using var manager = new TableManager();
        using var main = new MainForm(manager, startHidden: args.Contains("--minimized"));
        manager.Start();
        Application.Run(main);
    }
}
