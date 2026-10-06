namespace ApiKeyVault.UI.ViewModels;

public sealed class ProviderFilterItemViewModel
{
    public string Name { get; }
    public int Count { get; }

    public ProviderFilterItemViewModel(string name, int count)
    {
        Name = name;
        Count = count;
    }
}
