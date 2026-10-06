using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiKeyVault.UI.ViewModels;

public sealed partial class RecentVaultItemViewModel : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string DirectoryPath { get; }
    public bool Exists => File.Exists(FullPath);

    [ObservableProperty]
    private bool _isActive;

    public RecentVaultItemViewModel(string fullPath, bool isActive = false)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        DirectoryPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        _isActive = isActive;
    }
}
