using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ServiceMap.App.Services;

namespace ServiceMap.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = $"Version {AppInfo.Version}";
        CreditText.Text = $"Created by {AppInfo.Author}";
        CopyrightText.Text = $"© {AppInfo.Year} Carrier. For internal migration use.";

        try
        {
            var logo = Path.Combine(AppContext.BaseDirectory, "assets", "DependenSee.png");
            if (File.Exists(logo)) LogoImage.Source = new Bitmap(logo);
        }
        catch { /* logo optional */ }
    }

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateStatusText.IsVisible = true;
        UpdateStatusText.Text = "Checking…";
        try
        {
            var result = await UpdateChecker.CheckAsync();
            UpdateStatusText.Text = result.UpdateAvailable
                ? result.Message + $" Download from {UpdateChecker.ReleasesPage}"
                : result.Message;
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Could not check for updates: " + ex.Message;
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
