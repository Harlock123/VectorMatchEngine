namespace VectorMatchEngine.UI.Services;

/// <summary>
/// Wraps the window-bound dialog APIs (StorageProvider, modal confirmation) so ViewModels can
/// stay free of Window references. Avalonia has no built-in MessageBox, hence ConfirmAsync.
/// </summary>
public interface IDialogService
{
    Task<string?> OpenExcelFileAsync(string title = "Select an Excel workbook");
    Task<string?> SaveExcelFileAsync(string suggestedFileName, string title = "Export match results");
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete");
}
