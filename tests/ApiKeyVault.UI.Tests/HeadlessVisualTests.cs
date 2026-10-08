using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using ApiKeyVault.UI.ViewModels;
using ApiKeyVault.UI.Views;

namespace ApiKeyVault.UI.Tests;

public class HeadlessVisualTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;
    private readonly string _statePath;
    private readonly InMemoryDeviceKeyStore _deviceStore;
    private readonly LocalStateManager _stateManager;
    private readonly string _artifactDir = @"C:\Users\DandreJansenvanVuure\.gemini\antigravity-ide\brain\d6397232-02cf-4e8c-9b33-e4e05a89fbab";

    private static readonly Argon2idParameters FastKdf = new()
    {
        MemoryKiB = CryptoConstants.MinMemoryKiB,
        Iterations = CryptoConstants.MinIterations,
        Parallelism = 1
    };

    public HeadlessVisualTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AkvHeadless_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "demo-vault.akv");
        _statePath = Path.Combine(_tempDir, "state.json");
        _deviceStore = new InMemoryDeviceKeyStore();
        _stateManager = new LocalStateManager(_statePath);
    }

    [AvaloniaFact]
    public void RenderMainWindow_Normal_Narrow_Collapsed_And_ModalDialogs()
    {
        // 1. Create demo vault mirroring user's demo-vault.akv
        var createResult = VaultManager.CreateVault(_vaultPath, "P@ssw0rd", "TEST-MACHINE", FastKdf, _deviceStore, _stateManager);
        var session = createResult.Session;

        var today = DateTimeOffset.UtcNow;
        session.AddEntry("anthropic", "beta-key", "demo-anthropic-secret-123", comment: "Claude 3.5 Sonnet agent", expires: today.AddDays(3));
        session.AddEntry("aws", "s3-access", "demo-aws-s3-secret-456", comment: "Asset storage bucket", expires: today.AddDays(180));
        session.AddEntry("huggingface", "inference-api", "demo-hf-secret-789", comment: "Open-source embedding models", expires: today.AddDays(45));
        session.AddEntry("openai", "prod-key", "demo-openai-secret-abc", comment: "Primary API key", expires: today.AddDays(88));
        session.AddEntry("stripe", "live-api", "demo-stripe-secret-def", comment: "Production billing gateway", expires: today.AddDays(92));
        session.Dispose();

        // 2. Setup ViewModel and unlock
        var vm = new MainWindowViewModel(
            _deviceStore,
            _stateManager,
            defaultVaultPath: _vaultPath
        );
        vm.UnlockPassphrase = "P@ssw0rd";
        vm.UnlockCommand.Execute(null);
        Assert.True(vm.IsUnlocked);
        Assert.Equal(5, vm.TotalKeysCount);

        // --- 1. NORMAL VIEW (1080x720) ---
        var window = new MainWindow
        {
            DataContext = vm,
            Width = 1080,
            Height = 720
        };
        window.Show();

        var normalFrame = window.CaptureRenderedFrame();
        Assert.NotNull(normalFrame);
        normalFrame.Save(Path.Combine(_artifactDir, "1_normal_view_1080x720.png"));

        // --- 2. NARROW VIEW (880x650) with inspector visible ---
        window.Width = 880;
        window.Height = 650;
        var narrowFrame = window.CaptureRenderedFrame();
        Assert.NotNull(narrowFrame);
        narrowFrame.Save(Path.Combine(_artifactDir, "2_narrow_view_850x650.png"));

        // --- 3. COLLAPSED INSPECTOR VIEW (880x650) ---
        vm.IsInspectorVisible = false;
        var collapsedFrame = window.CaptureRenderedFrame();
        Assert.NotNull(collapsedFrame);
        collapsedFrame.Save(Path.Combine(_artifactDir, "3_collapsed_inspector_850x650.png"));

        // Verify auto-show inspector on entry selection
        vm.SelectedEntry = vm.FilteredEntries[2];
        Assert.True(vm.IsInspectorVisible);

        // --- 4. NEW KEY MODAL VIEW ---
        vm.OpenAddKeyDialogCommand.Execute(null);
        Assert.True(vm.IsAddKeyDialogOpen);
        Assert.False(vm.KeyDialogIsEditing);
        var newKeyModalFrame = window.CaptureRenderedFrame();
        Assert.NotNull(newKeyModalFrame);
        newKeyModalFrame.Save(Path.Combine(_artifactDir, "4_new_key_dialog_modal.png"));

        // --- 5. EDIT KEY MODAL VIEW ---
        vm.CancelKeyDialogCommand.Execute(null);
        vm.OpenEditKeyDialogCommand.Execute(null);
        Assert.True(vm.IsAddKeyDialogOpen);
        Assert.True(vm.KeyDialogIsEditing);
        var editKeyModalFrame = window.CaptureRenderedFrame();
        Assert.NotNull(editKeyModalFrame);
        editKeyModalFrame.Save(Path.Combine(_artifactDir, "5_edit_key_dialog_modal.png"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
