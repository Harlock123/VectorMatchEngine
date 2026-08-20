using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using VectorMatchEngine.UI.Views;

namespace VectorMatchEngine.UI.Services;

public class DialogService : IDialogService
{
    private static readonly FilePickerFileType ExcelFileType = new("Excel workbook (*.xlsx)")
    {
        Patterns = new[] { "*.xlsx" },
        AppleUniformTypeIdentifiers = new[] { "org.openxmlformats.spreadsheetml.sheet" },
        MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
    };

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> OpenExcelFileAsync(string title = "Select an Excel workbook")
    {
        var storage = MainWindow?.StorageProvider;
        if (storage is null)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { ExcelFileType }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveExcelFileAsync(string suggestedFileName, string title = "Export match results")
    {
        var storage = MainWindow?.StorageProvider;
        if (storage is null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xlsx",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { ExcelFileType }
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete")
    {
        var owner = MainWindow;
        if (owner is null)
            return false;

        var dialog = new ConfirmDialog
        {
            DataContext = new ConfirmDialogViewModel(title, message, confirmText)
        };

        return await dialog.ShowDialog<bool>(owner);
    }
}

public class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(string title, string message, string confirmText)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
}
