namespace ServiceMap.App.Services;

/// <summary>Abstraction over a native "Save As" dialog for the view models.</summary>
public interface IFileSaveService
{
    /// <summary>
    /// Prompt for a save path. Returns the chosen full path, or null if cancelled.
    /// </summary>
    Task<string?> SaveAsync(string suggestedName, string extension, string? initialDirectory = null);
}
