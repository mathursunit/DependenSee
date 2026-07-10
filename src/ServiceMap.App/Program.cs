using Avalonia;

namespace ServiceMap.App;

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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
