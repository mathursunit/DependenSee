using Avalonia;

namespace ServiceMap.App;

/// <summary>Process-wide startup flags read by the GUI.</summary>
internal static class AppModes
{
    /// <summary>--console: run as a viewer only, ignoring any local collector.</summary>
    public static bool ForceConsole { get; set; }
}

internal static class Program
{
    // Avalonia entry point. Keep initialization minimal here.
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless scheduled remote scan: no GUI.
        if (args.Length > 0 && args[0].Equals("remote-scan", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(RemoteScanCli.Run(args).GetAwaiter().GetResult());
            return;
        }
        // Headless dossier export: no GUI.
        if (args.Length > 0 && args[0].Equals("export-dossier", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(DossierCli.Run(args));
            return;
        }
        AppModes.ForceConsole = args.Contains("--console", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
