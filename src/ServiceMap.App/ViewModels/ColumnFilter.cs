using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ServiceMap.App.ViewModels;

/// <summary>One selectable value in a column-filter checklist.</summary>
public sealed partial class CheckedValue : ObservableObject
{
    private readonly Action _onChanged;
    public string Display { get; }

    public CheckedValue(string display, bool isChecked, Action onChanged)
    {
        Display = display;
        _isChecked = isChecked;   // set backing field: no callback during construction
        _onChanged = onChanged;
    }

    [ObservableProperty] private bool _isChecked;
    partial void OnIsCheckedChanged(bool value) => _onChanged();
}

/// <summary>
/// Excel-style per-column filter: a searchable checklist of the distinct values
/// seen in a column. Rows whose value is unchecked are hidden. The filter works
/// on the already-loaded result set, so toggling values re-filters instantly
/// without re-querying the database.
/// </summary>
public sealed partial class ColumnFilter : ObservableObject
{
    private readonly Func<object, string> _selector;
    private Action? _onApply;
    private bool _suppress;

    public string Header { get; }
    public ObservableCollection<CheckedValue> Values { get; } = new();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private bool _isActive;

    public ColumnFilter(string header, Func<object, string> selector)
    {
        Header = header;
        _selector = selector;
    }

    /// <summary>Callback invoked whenever the selection changes.</summary>
    public void Bind(Action onApply) => _onApply = onApply;

    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(FilteredValues));

    /// <summary>Values matching the in-popup search box.</summary>
    public IEnumerable<CheckedValue> FilteredValues =>
        string.IsNullOrEmpty(Search)
            ? Values
            : Values.Where(v => v.Display.Contains(Search, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rebuild the distinct-value list, preserving prior unchecked selections.</summary>
    public void Populate(IEnumerable<object> rows)
    {
        var unchecked_ = Values.Where(v => !v.IsChecked).Select(v => v.Display)
                               .ToHashSet(StringComparer.Ordinal);
        _suppress = true;
        Values.Clear();
        var distinct = rows.Select(r => _selector(r) ?? string.Empty)
                           .Distinct(StringComparer.Ordinal)
                           .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var val in distinct)
            Values.Add(new CheckedValue(val, !unchecked_.Contains(val), OnValueChanged));
        _suppress = false;
        UpdateActive();
        OnPropertyChanged(nameof(FilteredValues));
    }

    private void OnValueChanged()
    {
        if (_suppress) return;
        UpdateActive();
        _onApply?.Invoke();
    }

    private void UpdateActive() => IsActive = Values.Any(v => !v.IsChecked);

    [RelayCommand]
    private void SelectAll()
    {
        _suppress = true;
        foreach (var v in Values) v.IsChecked = true;
        _suppress = false;
        OnValueChanged();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _suppress = true;
        foreach (var v in Values) v.IsChecked = false;
        _suppress = false;
        OnValueChanged();
    }

    /// <summary>True when the row's value is currently checked (or the filter is inactive).</summary>
    public bool Accepts(object row)
    {
        if (!IsActive) return true;
        var val = _selector(row) ?? string.Empty;
        foreach (var v in Values)
            if (string.Equals(v.Display, val, StringComparison.Ordinal))
                return v.IsChecked;
        return true;
    }
}
