using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Manage user annotations (friendly name / owner / criticality) for processes,
/// ports, and hosts. Stored in the GUI workspace and used to enrich reports.
/// </summary>
public sealed partial class AnnotationsViewModel : ViewModelBase
{
    private readonly WorkspaceStore _store;

    public ObservableCollection<Annotation> Items { get; } = new();

    public string[] KindOptions { get; } = { "Process", "Port", "Host" };
    public string[] CriticalityOptions { get; } = { "Unset", "Low", "Medium", "High", "Critical" };

    [ObservableProperty] private string _selectedKind = "Process";
    [ObservableProperty] private string? _key;
    [ObservableProperty] private string? _friendlyName;
    [ObservableProperty] private string? _owner;
    [ObservableProperty] private string _selectedCriticality = "Unset";
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private Annotation? _selectedItem;

    public AnnotationsViewModel(WorkspaceStore store)
    {
        _store = store;
        Reload();
    }

    partial void OnSelectedItemChanged(Annotation? value)
    {
        if (value is null) return;
        SelectedKind = value.Kind.ToString();
        Key = value.Key;
        FriendlyName = value.FriendlyName;
        Owner = value.Owner;
        SelectedCriticality = value.Criticality.ToString();
        Notes = value.Notes;
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var a in _store.GetAnnotations()) Items.Add(a);
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Status = "Enter a key (process name, port, or host) first.";
            return;
        }
        var a = new Annotation
        {
            Kind = Enum.Parse<AnnotationKind>(SelectedKind),
            Key = Key!.Trim(),
            FriendlyName = Blank(FriendlyName),
            Owner = Blank(Owner),
            Criticality = Enum.Parse<Criticality>(SelectedCriticality),
            Notes = Blank(Notes)
        };
        _store.Upsert(a);
        Reload();
        Status = $"Saved {a.Kind} \"{a.Key}\".";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedItem is null) { Status = "Select a row to delete."; return; }
        _store.DeleteAnnotation(SelectedItem.Kind, SelectedItem.Key);
        Reload();
        Status = "Deleted.";
        New();
    }

    [RelayCommand]
    private void New()
    {
        Key = FriendlyName = Owner = Notes = null;
        SelectedCriticality = "Unset";
        SelectedItem = null;
        Status = "";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
