using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ServiceMap.App.Services;

/// <summary>Save dialog backed by the main window's Avalonia StorageProvider.</summary>
public sealed class FileSaveService : IDialogService
{
    private readonly Func<Window?> _window;

    public FileSaveService(Func<Window?> window) => _window = window;

    public async Task<string?> SaveAsync(string suggestedName, string extension, string? initialDirectory = null)
    {
        var window = _window();
        if (window is null) return null;

        var options = new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType($"{extension.ToUpperInvariant()} file")
                {
                    Patterns = new[] { "*." + extension }
                }
            }
        };

        if (!string.IsNullOrEmpty(initialDirectory))
        {
            try
            {
                var folder = await window.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
                if (folder is not null) options.SuggestedStartLocation = folder;
            }
            catch { /* ignore */ }
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenAsync(string title, string extension)
    {
        var window = _window();
        if (window is null) return null;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType($"{extension.ToUpperInvariant()} file") { Patterns = new[] { "*." + extension } }
            }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
