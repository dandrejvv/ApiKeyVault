using System.Text.Json.Serialization;

namespace ApiKeyVault.Core.Model;

public sealed class Stamp : IComparable<Stamp>
{
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("writer")]
    public string Writer { get; set; } = string.Empty;

    public int CompareTo(Stamp? other)
    {
        if (other is null) return 1;
        int timeComp = Time.CompareTo(other.Time);
        if (timeComp != 0) return timeComp;
        return string.Compare(Writer, other.Writer, StringComparison.Ordinal);
    }
}

public sealed class LockboxRegistryEntry
{
    [JsonPropertyName("lockbox_id")]
    public string LockboxId { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("last_used")]
    public DateTimeOffset? LastUsed { get; set; }
}

public sealed class RevokedLockboxEntry
{
    [JsonPropertyName("lockbox_id")]
    public string LockboxId { get; set; } = string.Empty;

    [JsonPropertyName("recipient_pub")]
    public string RecipientPub { get; set; } = string.Empty;

    [JsonPropertyName("removed_at")]
    public DateTimeOffset RemovedAt { get; set; }
}

public sealed class EntryTestResult
{
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class VaultEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("expires")]
    public DateTimeOffset? Expires { get; set; }

    [JsonPropertyName("review_by")]
    public DateTimeOffset? ReviewBy { get; set; }

    [JsonPropertyName("extra_fields")]
    public Dictionary<string, string>? ExtraFields { get; set; }

    [JsonPropertyName("stamp")]
    public Stamp Stamp { get; set; } = new();

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;

    [JsonPropertyName("secret_stamp")]
    public Stamp SecretStamp { get; set; } = new();

    [JsonPropertyName("previous_secret")]
    public string? PreviousSecret { get; set; }

    [JsonPropertyName("previous_until")]
    public DateTimeOffset? PreviousUntil { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("last_used")]
    public DateTimeOffset? LastUsed { get; set; }

    [JsonPropertyName("last_test")]
    public EntryTestResult? LastTest { get; set; }

    [JsonPropertyName("is_compromised")]
    public bool IsCompromised { get; set; }

    [JsonPropertyName("is_revoked")]
    public bool IsRevoked { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

public sealed class ProfileMapping
{
    [JsonPropertyName("var")]
    public string Var { get; set; } = string.Empty;

    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = string.Empty;

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

public sealed class VaultProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mappings")]
    public List<ProfileMapping> Mappings { get; set; } = [];

    [JsonPropertyName("stamp")]
    public Stamp Stamp { get; set; } = new();
}

public sealed class VaultSetting
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("stamp")]
    public Stamp Stamp { get; set; } = new();
}

public sealed class Tombstone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("stamp")]
    public Stamp Stamp { get; set; } = new();
}

public sealed class VaultPayload
{
    [JsonPropertyName("identity_secret")]
    public string IdentitySecret { get; set; } = string.Empty;

    [JsonPropertyName("save_counter")]
    public ulong SaveCounter { get; set; }

    [JsonPropertyName("lockbox_registry")]
    public List<LockboxRegistryEntry> LockboxRegistry { get; set; } = [];

    [JsonPropertyName("revoked")]
    public List<RevokedLockboxEntry> Revoked { get; set; } = [];

    [JsonPropertyName("entries")]
    public List<VaultEntry> Entries { get; set; } = [];

    [JsonPropertyName("profiles")]
    public List<VaultProfile> Profiles { get; set; } = [];

    [JsonPropertyName("settings")]
    public Dictionary<string, VaultSetting> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("tombstones")]
    public List<Tombstone> Tombstones { get; set; } = [];
}
