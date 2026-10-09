using CommunityToolkit.Mvvm.ComponentModel;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Presets;

namespace ApiKeyVault.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

public sealed partial class EntryItemViewModel : ObservableObject
{
    public VaultEntry Entry { get; }

    public string Id => Entry.Id;
    public string Provider => Entry.Provider;
    public string Name => Entry.Name;
    public string Address => $"{Entry.Provider}/{Entry.Name}";
    public string? Comment => Entry.Comment;
    public string? Source => Entry.Source;
    public DateTimeOffset Created => Entry.Created;
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    public bool IsExpiringSoon => Status == KeyStatus.Due;
    public bool IsExpired => Status == KeyStatus.Expired;

    public bool IsCompromised => Entry.IsCompromised;
    public bool IsRevoked => Entry.IsRevoked;
    public string RevokeButtonText => IsRevoked ? "Restore Key" : "Revoke Key";

    public KeyStatus Status => EntryStatusCalculator.Compute(Entry, DateTimeOffset.UtcNow);

    public string StatusIcon => Status switch
    {
        KeyStatus.Compromised => "⚠️",
        KeyStatus.Revoked => "⊘",
        KeyStatus.Ok => "●",
        KeyStatus.Due => "▲",
        KeyStatus.Expired => "▲",
        KeyStatus.Failing => "✕",
        _ => "●"
    };

    public string StatusColor => Status switch
    {
        KeyStatus.Compromised => "#ef4444", // Bright Red / Danger
        KeyStatus.Revoked => "#64748b",     // Slate / Muted
        KeyStatus.Ok => "#10b981",          // Emerald
        KeyStatus.Due => "#f59e0b",         // Amber Warning
        KeyStatus.Expired => "#ef4444",     // Red Critical
        KeyStatus.Failing => "#ef4444",     // Red Critical
        _ => "#86948a"
    };

    /// <summary>Translucent version of <see cref="StatusColor"/> for status pills.</summary>
    public string StatusBackground => "#1F" + StatusColor.TrimStart('#');

    public string StatusBorder => "#40" + StatusColor.TrimStart('#');

    public string ExpiryColor => Status switch
    {
        KeyStatus.Due => "#f59e0b",
        KeyStatus.Expired => "#f87171",
        _ => "#94a3b8"
    };

    public string ExpiryDescription
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            if (Entry.Expires.HasValue)
            {
                int days = (int)Math.Ceiling((Entry.Expires.Value - now).TotalDays);
                return days >= 0 ? $"expires in {days} days" : $"expired {Math.Abs(days)} days ago";
            }
            if (Entry.ReviewBy.HasValue)
            {
                int days = (int)Math.Ceiling((Entry.ReviewBy.Value - now).TotalDays);
                return days >= 0 ? $"review in {days} days" : $"review due";
            }
            return "No expiry set";
        }
    }

    public string ExpiryTableDescription
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            if (Entry.Expires.HasValue)
            {
                int days = (int)Math.Ceiling((Entry.Expires.Value - now).TotalDays);
                return days >= 0 ? $"{days}d" : $"{Math.Abs(days)}d ago";
            }
            if (Entry.ReviewBy.HasValue)
            {
                int days = (int)Math.Ceiling((Entry.ReviewBy.Value - now).TotalDays);
                return days >= 0 ? $"rev {days}d" : "rev due";
            }
            return "—";
        }
    }

    public bool IsTestSupported => HttpProviderTester.IsSupported(Provider);

    public string TestStatusDescription
    {
        get
        {
            if (Entry.LastTest != null && (Entry.LastTest.Success || EntryStatusCalculator.IsFailedTest(Entry.LastTest)))
            {
                return Entry.LastTest.Success
                    ? $"✓ Passed ({Entry.LastTest.Time:yyyy-MM-dd HH:mm})"
                    : $"✕ Failed: {Entry.LastTest.Message}";
            }

            return IsTestSupported ? "Not tested yet" : "No automated test";
        }
    }

    public string TestSupportTooltip => IsTestSupported
        ? $"Send test request to verify {ProviderDisplayName} key."
        : $"Automated test not available for {ProviderDisplayName}. Supported: OpenAI, Anthropic, Gemini, OpenRouter, Azure OpenAI, Clockify, GitHub, GitLab.";

    public string StatusText => Status switch
    {
        KeyStatus.Compromised => "compromised",
        KeyStatus.Revoked => "revoked",
        KeyStatus.Ok => "active",
        KeyStatus.Due => "expiring",
        KeyStatus.Expired => "expired",
        KeyStatus.Failing => "failing",
        _ => "active"
    };

    public IReadOnlyList<string> Tags => EntryTags.Derive(Entry);

    public bool HasTags => Tags.Count > 0;
    public bool HasNoTags => Tags.Count == 0;

    public IReadOnlyList<TagBadgeViewModel> TagBadges =>
        Tags.OrderBy(GetTagSortPriority)
            .Select(t => new TagBadgeViewModel(t))
            .ToList();

    public IReadOnlyList<TagBadgeViewModel> VisibleTagBadges
    {
        get
        {
            var badges = TagBadges;
            if (badges.Count <= 2) return badges;
            if (badges.Count == 3 && badges.Sum(b => b.Name.Length) <= 12) return badges;
            return badges.Take(2).ToList();
        }
    }

    public int OverflowTagCount => Math.Max(0, TagBadges.Count - VisibleTagBadges.Count);
    public bool HasOverflowTags => OverflowTagCount > 0;
    public string OverflowTagText => $"+{OverflowTagCount}";

    private static int GetTagSortPriority(string tag) => tag switch
    {
        "intent" => 1,
        "personal" => 2,
        "prod" or "live" => 3,
        "staging" or "stag" => 4,
        "git" => 5,
        "embeddings" or "ai" => 6,
        "dev" => 10,
        _ => 20
    };

    public string TagsDisplay => Tags.Count > 0 ? string.Join(", ", TagBadges.Select(t => t.DisplayText)) : "No tags";

    public string PrimaryTag
    {
        get
        {
            // Give prominent priority to domain tags (intent, personal, prod, etc.)
            if (Tags.Contains("intent")) return "INTENT";
            if (Tags.Contains("personal")) return "PERSONAL";
            if (Tags.Contains("prod") || Tags.Contains("live")) return "PROD";
            if (Tags.Contains("staging") || Tags.Contains("stag")) return "STAGING";
            if (Tags.Contains("dev")) return "DEV";
            return Tags.Count > 0 ? Tags[0].ToUpperInvariant() : "KEY";
        }
    }

    public string PrimaryTagColor => TagBadgeViewModel.GetTagColors(PrimaryTag.ToLowerInvariant()).fg;

    public string EnvironmentTag => PrimaryTag;
    public string EnvironmentTagColor => PrimaryTagColor;

    public string ProviderDisplayName => ProviderVisuals.GetDisplayName(Provider);

    public string ProviderInitial => ProviderVisuals.GetInitial(Provider);
    public string ProviderColor => ProviderVisuals.GetColors(Provider).fg;
    public string ProviderTint => ProviderVisuals.GetColors(Provider).bg;
    public string ProviderBorder => ProviderVisuals.GetColors(Provider).border;

    public string Subtitle => HasComment ? Comment! : ProviderDisplayName;

    public EntryItemViewModel(VaultEntry entry)
    {
        Entry = entry;
    }
}
