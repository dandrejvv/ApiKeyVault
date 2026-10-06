using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ApiKeyVault.UI.Services;
using ApiKeyVault.UI.ViewModels;
using ApiKeyVault.UI.Views;

namespace ApiKeyVault.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = null!;
            var storageService = new AvaloniaStorageDialogService(() => window);
            var mainVm = new MainWindowViewModel(storageService: storageService);
            window = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.MainWindow = window;
            window.Show();
            window.Activate();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
