using ApiKeyVault.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("akv");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create a new vault");

    config.AddCommand<JoinCommand>("join")
        .WithDescription("Use the vault on another device");

    config.AddCommand<ShellCommand>("shell")
        .WithDescription("Open an interactive shell session");

    config.AddCommand<ImportCommand>("import")
        .WithDescription("Import keys from a plain-text file");

    config.AddCommand<ListCommand>("list")
        .WithAlias("ls")
        .WithDescription("List keys in the vault");

    config.AddCommand<FindCommand>("find")
        .WithDescription("Find keys by search text");

    config.AddCommand<GetCommand>("get")
        .WithDescription("Copy or reveal a key secret");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Run a command with keys injected as environment variables");

    config.AddCommand<AddCommand>("add")
        .WithDescription("Add a new key to the vault");

    config.AddCommand<EditCommand>("edit")
        .WithDescription("Edit key details");

    config.AddCommand<RotateCommand>("rotate")
        .WithDescription("Rotate a key secret");

    config.AddCommand<RemoveCommand>("rm")
        .WithAlias("remove")
        .WithDescription("Delete a key from the vault");

    config.AddCommand<TestCommand>("test")
        .WithDescription("Run live test on a key or all keys");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Display vault status and attention items");

    config.AddCommand<ProfileCommand>("profile")
        .WithDescription("Manage run profiles (list, set, rm)");

    config.AddBranch("access", access =>
    {
        access.SetDescription("Manage vault access (devices and lockboxes)");
        access.AddCommand<AccessListCommand>("list")
            .WithDescription("List devices and lockboxes with access");
        access.AddCommand<AccessRemoveCommand>("remove")
            .WithDescription("Remove device access and rotate vault key");
        access.AddCommand<AccessRenameCommand>("rename")
            .WithDescription("Rename this device");
    });

    config.AddBranch("passphrase", passphrase =>
    {
        passphrase.SetDescription("Manage vault passphrase");
        passphrase.AddCommand<PassphraseChangeCommand>("change")
            .WithDescription("Change the vault passphrase");
    });

    config.AddBranch("recovery", recovery =>
    {
        recovery.SetDescription("Manage vault recovery code");
        recovery.AddCommand<RecoveryNewCommand>("new")
            .WithDescription("Generate a new recovery code");
    });

    config.AddCommand<RecoverCommand>("recover")
        .WithDescription("Recover vault using recovery code");

    config.AddCommand<ConfigCommand>("config")
        .WithDescription("Manage local settings (get, set, list)");
});

return app.Run(args);
