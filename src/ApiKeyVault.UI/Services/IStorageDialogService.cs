using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ApiKeyVault.UI.Services;

public interface IStorageDialogService
{
    Task<string?> PickFolderAsync(string title, string? initialDirectory = null);
    Task<string?> PickOpenFileAsync(string title, string filterName, string[] extensions, string? initialDirectory = null);
}

public sealed class AvaloniaStorageDialogService : IStorageDialogService
{
    private readonly Func<Window?> _windowProvider;

    public AvaloniaStorageDialogService(Func<Window?> windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
    {
        var window = _windowProvider();
        if (window == null) return null;

        var storage = TopLevel.GetTopLevel(window)?.StorageProvider;
        if (storage == null) return null;

        IStorageFolder? suggestedStart = null;
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            suggestedStart = await storage.TryGetFolderFromPathAsync(initialDirectory);
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStart
        });

        if (folders.Count > 0)
        {
            return folders[0].TryGetLocalPath();
        }

        return null;
    }

    public async Task<string?> PickOpenFileAsync(string title, string filterName, string[] extensions, string? initialDirectory = null)
    {
        var window = _windowProvider();
        if (window == null) return null;

        var storage = TopLevel.GetTopLevel(window)?.StorageProvider;
        if (storage == null) return null;

        IStorageFolder? suggestedStart = null;
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            suggestedStart = await storage.TryGetFolderFromPathAsync(initialDirectory);
        }

        var fileTypes = new[]
        {
            new FilePickerFileType(filterName)
            {
                Patterns = extensions
            },
            new FilePickerFileType("All Files")
            {
                Patterns = ["*.*"]
            }
        };

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
            SuggestedStartLocation = suggestedStart
        });

        if (files.Count > 0)
        {
            return files[0].TryGetLocalPath();
        }

        return null;
    }
}
