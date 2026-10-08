namespace ApiKeyVault.UI.ViewModels;

public sealed class ProviderFilterItemViewModel
{
    public string Name { get; }
    public int Count { get; }
    public bool IsActive { get; }
    public string DisplayName => ProviderVisuals.GetDisplayName(Name);
    public string Initial => ProviderVisuals.GetInitial(Name);
    public string Color => ProviderVisuals.GetColors(Name).fg;
    public string Tint => ProviderVisuals.GetColors(Name).bg;

    public ProviderFilterItemViewModel(string name, int count, bool isActive = false)
    {
        IsActive = isActive;
        Name = name;
        Count = count;
    }
}
