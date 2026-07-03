using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Net;

namespace ServiceMap.App.ViewModels;

/// <summary>Live view of the most recent service snapshot and socket sweep.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly DataAccess _data;

    public ObservableCollection<ServiceRecord> Services { get; } = new();
    public ObservableCollection<ConnectionSample> Listeners { get; } = new();
    public ObservableCollection<ConnectionSample> ActiveConnections { get; } = new();

    [ObservableProperty] private int _serviceCount;
    [ObservableProperty] private int _listenerCount;
    [ObservableProperty] private int _inboundCount;
    [ObservableProperty] private int _outboundCount;
    [ObservableProperty] private string _statusLine = "Waiting for data…";
    [ObservableProperty] private bool _hideStandardServices;

    partial void OnHideStandardServicesChanged(bool value) => Refresh();

    public DashboardViewModel(DataAccess data) => _data = data;

    public void Refresh()
    {
        if (!_data.DatabaseExists)
        {
            StatusLine = "No database yet. Install and start the collector service (Settings tab).";
            return;
        }

        var services = _data.GetLatestServices();
        var connections = _data.GetLatestConnections();

        var shownServices = HideStandardServices
            ? services.Where(sv => !WindowsServiceClassifier.IsStandard(sv)).ToList()
            : (IReadOnlyList<ServiceRecord>)services;
        Replace(Services, shownServices);
        ServiceCount = shownServices.Count;

        var listeners = connections
            .Where(c => c.Direction == ConnectionDirection.Listen)
            .OrderBy(c => c.LocalPort).ToList();
        var active = connections
            .Where(c => c.Direction is ConnectionDirection.Inbound or ConnectionDirection.Outbound)
            .OrderBy(c => c.ProcessName).ToList();

        Replace(Listeners, listeners);
        Replace(ActiveConnections, active);

        ListenerCount = listeners.Count;
        InboundCount = active.Count(c => c.Direction == ConnectionDirection.Inbound);
        OutboundCount = active.Count(c => c.Direction == ConnectionDirection.Outbound);

        var latest = connections.Count > 0 ? connections[0].Timestamp.ToLocalTime().ToString("g") : "—";
        StatusLine = $"Last sweep: {latest}   •   {ServiceCount} services, " +
                     $"{ListenerCount} listeners, {InboundCount} inbound, {OutboundCount} outbound";
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
