using Avalonia;
using Avalonia.Headless;
using ApiKeyVault.UI;
using ApiKeyVault.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ApiKeyVault.UI.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .WithInterFont();
}
