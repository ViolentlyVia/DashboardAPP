using PulsePoint.Agent;

// Seed config from command-line args (used by installer)
// --server-url http://... --interval 30 --ip 192.168.1.x
if (args.Length > 0)
{
    var config = ConfigStore.Load();
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--server-url")  config.ServerUrl      = args[i + 1];
        if (args[i] == "--interval")    config.IntervalSeconds = int.TryParse(args[i + 1], out var n) ? n : 30;
        if (args[i] == "--ip")          config.PreferredIp    = args[i + 1];
    }
    ConfigStore.Save(config);
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.Run(new TrayContext());
