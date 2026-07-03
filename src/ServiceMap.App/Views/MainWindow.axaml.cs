using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ServiceMap.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TryLoadBrandIcon();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }

    /// <summary>Use the Carrier DependenSee icon if it was deployed next to the exe.</summary>
    private void TryLoadBrandIcon()
    {
        try
        {
            foreach (var name in new[] { "DependenSee.ico", "DependenSee.png" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "assets", name);
                if (File.Exists(path))
                {
                    Icon = new WindowIcon(path);
                    return;
                }
            }
        }
        catch
        {
            // Non-fatal: fall back to the default window icon.
        }
    }
}
