using System.Reflection;

namespace ServiceMap.App.Services;

/// <summary>Product identity read from the assembly, for the About box.</summary>
public static class AppInfo
{
    public const string ProductName = "Carrier DependenSee";
    public const string Tagline = "See what connects.";
    public const string Author = "Sunit Mathur";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // Strip any "+<git-hash>" build metadata.
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "1.0";
        }
    }

    public static int Year => DateTime.Now.Year;
}
