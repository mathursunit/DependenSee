namespace ServiceMap.App.Services;

/// <summary>Abstraction over a native "Open File" dialog.</summary>
public interface IFileOpenService
{
    Task<string?> OpenAsync(string title, string extension);
}

/// <summary>Combined save + open dialogs, provided by one implementation.</summary>
public interface IDialogService : IFileSaveService, IFileOpenService { }
