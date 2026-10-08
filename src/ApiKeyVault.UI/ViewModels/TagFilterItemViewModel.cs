namespace ApiKeyVault.UI.ViewModels;

public sealed class TagFilterItemViewModel
{
    public string Tag { get; }
    public string DisplayTag => $"#{Tag}";
    public int Count { get; }
    public string Color => TagBadgeViewModel.GetTagColors(Tag).fg;

    public TagFilterItemViewModel(string tag, int count)
    {
        Tag = tag;
        Count = count;
    }
}
