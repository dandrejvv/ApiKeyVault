using System.Text.Json.Serialization;

namespace ApiKeyVault.Core.Model;

public sealed class VaultHeader
{
    [JsonPropertyName("vault_id")]
    public string VaultId { get; set; } = string.Empty;

    [JsonPropertyName("lockboxes")]
    public List<LockboxModel> Lockboxes { get; set; } = [];
}

public sealed class LockboxModel
{
    [JsonPropertyName("lockbox_id")]
    public string LockboxId { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("recipient_pub")]
    public string RecipientPub { get; set; } = string.Empty;

    [JsonPropertyName("eph_pub")]
    public string EphPub { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("wrapped_vault_key")]
    public string WrappedVaultKey { get; set; } = string.Empty;

    [JsonPropertyName("kdf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KdfModel? Kdf { get; set; }

    [JsonPropertyName("id_tag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdTag { get; set; }
}

public sealed class KdfModel
{
    [JsonPropertyName("alg")]
    public string Alg { get; set; } = "argon2id";

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    [JsonPropertyName("m")]
    public int M { get; set; }

    [JsonPropertyName("t")]
    public int T { get; set; }

    [JsonPropertyName("p")]
    public int P { get; set; } = 1;
}
