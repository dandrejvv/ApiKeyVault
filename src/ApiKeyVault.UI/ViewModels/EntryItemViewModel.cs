using CommunityToolkit.Mvvm.ComponentModel;
using ApiKeyVault.Core.Model;

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

    public KeyStatus Status => EntryStatusCalculator.Compute(Entry, DateTimeOffset.UtcNow);

    public string StatusIcon => Status switch
    {
        KeyStatus.Ok => "●",
        KeyStatus.Due => "▲",
        KeyStatus.Expired => "▲",
        KeyStatus.Failing => "✕",
        _ => "●"
    };

    public string StatusColor => Status switch
    {
        KeyStatus.Ok => "#10b981",      // Emerald
        KeyStatus.Due => "#f59e0b",     // Amber Warning
        KeyStatus.Expired => "#ef4444", // Red Critical
        KeyStatus.Failing => "#ef4444", // Red Critical
        _ => "#86948a"
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

    public string TestStatusDescription
    {
        get
        {
            if (Entry.LastTest == null) return "Not tested";
            return Entry.LastTest.Success
                ? $"✓ Passed ({Entry.LastTest.Time:yyyy-MM-dd HH:mm})"
                : $"✕ Failed: {Entry.LastTest.Message}";
        }
    }

    public string StatusText => Status switch
    {
        KeyStatus.Ok => "active",
        KeyStatus.Due => "expiring",
        KeyStatus.Expired => "expired",
        KeyStatus.Failing => "failing",
        _ => "active"
    };

    public string EnvironmentTag
    {
        get
        {
            if (Name.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
                Address.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
                Address.Contains("live", StringComparison.OrdinalIgnoreCase))
            {
                return "PROD";
            }
            if (Name.Contains("stag", StringComparison.OrdinalIgnoreCase) ||
                Address.Contains("stag", StringComparison.OrdinalIgnoreCase))
            {
                return "STAGING";
            }
            return "DEV";
        }
    }

    public string EnvironmentTagColor => EnvironmentTag switch
    {
        "PROD" => "#10b981",
        "STAGING" => "#f59e0b",
        _ => "#60a5fa"
    };

    public string ProviderDisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Provider)) return "Other";
            return Provider switch
            {
                "openai" => "OpenAI",
                "anthropic" => "Anthropic",
                "openrouter" => "OpenRouter",
                "azure" => "Azure",
                "gemini" => "Gemini",
                "stripe" => "Stripe",
                "aws" => "AWS",
                "github" => "GitHub",
                _ => char.ToUpper(Provider[0]) + Provider.Substring(1)
            };
        }
    }

    public EntryItemViewModel(VaultEntry entry)
    {
        Entry = entry;
    }
}
