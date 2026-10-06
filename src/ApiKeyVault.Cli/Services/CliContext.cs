using System.Text.Json;
using ApiKeyVault.Cli.Commands;
using ApiKeyVault.Core;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using Spectre.Console;

namespace ApiKeyVault.Cli.Services;

public static class CliContext
{
    public static string ResolveVaultPath(GlobalSettings settings, LocalStateManager stateManager)
    {
        if (!string.IsNullOrWhiteSpace(settings.VaultPath))
        {
            return Path.GetFullPath(settings.VaultPath);
        }

        var state = stateManager.Load();
        if (!string.IsNullOrWhiteSpace(state.Settings.DefaultVaultPath) && File.Exists(state.Settings.DefaultVaultPath))
        {
            return state.Settings.DefaultVaultPath;
        }

        // Try OneDrive or local directory
        string localVault = Path.GetFullPath("vault.akv");
        if (File.Exists(localVault))
        {
            return localVault;
        }

        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive))
        {
            string odVault = Path.Combine(oneDrive, "ApiKeyVault", "vault.akv");
            if (File.Exists(odVault))
            {
                return odVault;
            }
        }

        throw new VaultNotFoundException(settings.VaultPath ?? "vault.akv");
    }

    public static VaultSession OpenSession(
        GlobalSettings settings,
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? stateManager = null)
    {
        deviceKeyStore ??= DeviceKeyStoreFactory.CreateDefault();
        stateManager ??= new LocalStateManager();

        string vaultPath = ResolveVaultPath(settings, stateManager);

        // 1. Try unlocking with device key (Tier 1 silent unlock)
        try
        {
            return VaultManager.OpenWithDevice(
                vaultPath, deviceKeyStore, stateManager, acceptRollback: settings.AcceptRollback);
        }
        catch (VaultRollbackException)
        {
            throw;
        }
        catch (VaultTamperedException)
        {
            throw;
        }
        catch (UnsupportedVaultVersionException)
        {
            throw;
        }
        catch (VaultAccessDeniedException)
        {
            // Device not enrolled or removed, fall through to passphrase unlock
        }

        // 2. Unlock with passphrase
        string? passphrase = null;
        if (settings.PassphraseStdin)
        {
            passphrase = Console.ReadLine();
        }
        else if (!settings.NoInput && !Console.IsInputRedirected)
        {
            passphrase = AnsiConsole.Prompt(
                new TextPrompt<string>("Passphrase: ").Secret());
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new VaultAccessDeniedException("Device is not enrolled and no passphrase was supplied.");
        }

        return VaultManager.OpenWithPassphrase(
            vaultPath, passphrase, deviceKeyStore, stateManager, acceptRollback: settings.AcceptRollback);
    }

    public static int HandleException(Exception ex, bool outputJson = false)
    {
        int exitCode = ex switch
        {
            UnsupportedVaultVersionException => 8,
            VaultRollbackException => 7,
            VaultTamperedException => 6,
            KeyNotFoundException => 4,
            VaultAccessDeniedException => 3,
            ArgumentException => 2,
            _ => 1
        };

        if (outputJson)
        {
            var err = new { error = ex.Message, exit_code = exitCode };
            Console.Error.WriteLine(JsonSerializer.Serialize(err));
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        }

        return exitCode;
    }
}
