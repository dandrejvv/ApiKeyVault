using System.Security.Cryptography;
using System.Text;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Presets;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Sync;

namespace ApiKeyVault.Core.Vault;

public sealed class VaultSession : IDisposable
{
    private readonly string _vaultPath;
    private byte[] _vaultKey;
    private readonly string _activeLockboxId;
    private readonly bool _isDeviceEnrolled;
    private readonly IDeviceKeyStore _deviceKeyStore;
    private readonly LocalStateManager _localStateManager;
    private bool _disposed;

    public VaultHeader Header { get; private set; }
    public VaultPayload Payload { get; private set; }
    public string VaultPath => _vaultPath;
    public string ActiveLockboxId => _activeLockboxId;
    public bool IsDeviceEnrolled => _isDeviceEnrolled;

    internal VaultSession(
        string vaultPath,
        byte[] vaultKey,
        VaultHeader header,
        VaultPayload payload,
        string activeLockboxId,
        bool isDeviceEnrolled,
        IDeviceKeyStore deviceKeyStore,
        LocalStateManager localStateManager)
    {
        _vaultPath = vaultPath;
        _vaultKey = vaultKey;
        Header = header;
        Payload = payload;
        _activeLockboxId = activeLockboxId;
        _isDeviceEnrolled = isDeviceEnrolled;
        _deviceKeyStore = deviceKeyStore;
        _localStateManager = localStateManager;
    }

    public string CurrentDeviceName
    {
        get
        {
            var reg = Payload.LockboxRegistry.FirstOrDefault(r => r.LockboxId == _activeLockboxId);
            return reg?.Name ?? Environment.MachineName;
        }
    }

    public ReadOnlySpan<byte> VaultKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _vaultKey;
        }
    }

    public List<VaultEntry> GetEntries() => Payload.Entries;

    public VaultEntry? FindEntry(string addressOrId, bool allowShortName = false)
    {
        if (string.IsNullOrWhiteSpace(addressOrId)) return null;

        string query = addressOrId.Trim();

        // 1. Match by ID
        var byId = Payload.Entries.FirstOrDefault(e => string.Equals(e.Id, query, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;

        // 2. Match by exact provider/name
        if (query.Contains('/'))
        {
            string[] parts = query.Split('/', 2);
            var byAddress = Payload.Entries.FirstOrDefault(e =>
                string.Equals(e.Provider, parts[0], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Name, parts[1], StringComparison.OrdinalIgnoreCase));
            if (byAddress != null) return byAddress;
        }

        // 3. Match short name if enabled
        if (allowShortName)
        {
            var matches = Payload.Entries.Where(e =>
                string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];

            // Fuzzy match name or provider/name
            var fuzzyMatches = Payload.Entries.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                $"{e.Provider}/{e.Name}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fuzzyMatches.Count == 1) return fuzzyMatches[0];
        }

        return null;
    }

    public List<VaultEntry> SearchEntries(
        string? query = null,
        string? provider = null,
        bool dueOnly = false,
        bool failingOnly = false,
        string? tag = null,
        bool attentionOnly = false)
    {
        var now = DateTimeOffset.UtcNow;
        var queryable = Payload.Entries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(provider))
        {
            queryable = queryable.Where(e => string.Equals(e.Provider, provider, StringComparison.OrdinalIgnoreCase));
        }

        if (failingOnly)
        {
            queryable = queryable.Where(e => EntryStatusCalculator.IsFailedTest(e.LastTest));
        }

        if (dueOnly)
        {
            queryable = queryable.Where(e =>
            {
                var status = EntryStatusCalculator.Compute(e, now);
                return status == KeyStatus.Due || status == KeyStatus.Expired;
            });
        }

        if (attentionOnly)
        {
            queryable = queryable.Where(e => EntryStatusCalculator.NeedsAttention(EntryStatusCalculator.Compute(e, now)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            queryable = queryable.Where(e => EntryTags.HasTag(e, tag));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            queryable = queryable.Where(e =>
                $"{e.Provider}/{e.Name}".Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (e.Comment != null && e.Comment.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                EntryTags.Derive(e).Any(t => t.Contains(q.TrimStart('#'), StringComparison.OrdinalIgnoreCase)));
        }

        return queryable.OrderBy(e => e.Provider).ThenBy(e => e.Name).ToList();
    }

    public VaultEntry AddEntry(
        string provider,
        string name,
        string secret,
        string? comment = null,
        string? source = null,
        DateTimeOffset? expires = null,
        DateTimeOffset? reviewBy = null,
        Dictionary<string, string>? extraFields = null,
        bool isCompromised = false,
        bool isRevoked = false,
        List<string>? tags = null)
    {
        string normProvider = provider.Trim().ToLowerInvariant();
        string normName = name.Trim().ToLowerInvariant();

        if (Payload.Entries.Any(e => string.Equals(e.Provider, normProvider, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(e.Name, normName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"An entry already exists for '{normProvider}/{normName}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var stamp = new Stamp { Time = now, Writer = _activeLockboxId };

        var entry = new VaultEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Provider = normProvider,
            Name = normName,
            Comment = comment,
            Source = source,
            Expires = expires,
            ReviewBy = reviewBy,
            ExtraFields = extraFields,
            IsCompromised = isCompromised,
            IsRevoked = isRevoked,
            Tags = tags ?? [],
            Stamp = stamp,
            Secret = secret,
            SecretStamp = stamp,
            Created = now,
            LastUsed = null,
            LastTest = null
        };

        Payload.Entries.Add(entry);
        Save();
        return entry;
    }

    public void EditEntry(
        string idOrAddress,
        string? newName = null,
        string? newComment = null,
        string? newSource = null,
        DateTimeOffset? newExpires = null,
        DateTimeOffset? newReviewBy = null,
        Dictionary<string, string>? newExtraFields = null,
        bool? isCompromised = null,
        bool? isRevoked = null,
        List<string>? tags = null,
        bool clearExpires = false,
        bool clearReviewBy = false)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(entry.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            string normName = newName.Trim().ToLowerInvariant();
            if (Payload.Entries.Any(e => e.Id != entry.Id &&
                                         string.Equals(e.Provider, entry.Provider, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(e.Name, normName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"An entry already exists for '{entry.Provider}/{normName}'.");
            }
            entry.Name = normName;
        }

        if (newComment != null) entry.Comment = newComment;
        if (newSource != null) entry.Source = newSource;
        if (newExpires.HasValue) entry.Expires = newExpires;
        if (newReviewBy.HasValue) entry.ReviewBy = newReviewBy;
        if (clearExpires) entry.Expires = null;
        if (clearReviewBy) entry.ReviewBy = null;
        if (newExtraFields != null) entry.ExtraFields = newExtraFields;
        if (isCompromised.HasValue) entry.IsCompromised = isCompromised.Value;
        if (isRevoked.HasValue) entry.IsRevoked = isRevoked.Value;
        if (tags != null) entry.Tags = tags;

        entry.Stamp = new Stamp { Time = DateTimeOffset.UtcNow, Writer = _activeLockboxId };
        Save();
    }

    public void SetEntryCompromised(string idOrAddress, bool isCompromised)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        entry.IsCompromised = isCompromised;
        entry.Stamp = new Stamp { Time = DateTimeOffset.UtcNow, Writer = _activeLockboxId };
        Save();
    }

    public void SetEntryRevoked(string idOrAddress, bool isRevoked)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        entry.IsRevoked = isRevoked;
        entry.Stamp = new Stamp { Time = DateTimeOffset.UtcNow, Writer = _activeLockboxId };
        Save();
    }

    public void RotateSecret(string idOrAddress, string newSecret)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        var now = DateTimeOffset.UtcNow;
        entry.PreviousSecret = entry.Secret;
        entry.PreviousUntil = now.AddDays(7);
        entry.Secret = newSecret;
        entry.SecretStamp = new Stamp { Time = now, Writer = _activeLockboxId };

        Save();
    }

    public void UndoRotation(string idOrAddress)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        if (string.IsNullOrEmpty(entry.PreviousSecret))
        {
            throw new InvalidOperationException("No previous secret available to restore.");
        }

        var now = DateTimeOffset.UtcNow;
        entry.Secret = entry.PreviousSecret;
        entry.PreviousSecret = null;
        entry.PreviousUntil = null;
        entry.SecretStamp = new Stamp { Time = now, Writer = _activeLockboxId };

        Save();
    }

    public void DeleteEntry(string idOrAddress)
    {
        var entry = FindEntry(idOrAddress, allowShortName: false)
            ?? throw new KeyNotFoundException($"Entry '{idOrAddress}' not found.");

        var now = DateTimeOffset.UtcNow;
        Payload.Tombstones.Add(new Tombstone
        {
            Id = entry.Id,
            Stamp = new Stamp { Time = now, Writer = _activeLockboxId }
        });

        Payload.Entries.Remove(entry);
        Save();
    }

    public void RecordEntryUsed(VaultEntry entry)
    {
        var now = DateTimeOffset.UtcNow;
        // Throttle to at most once a day per entry
        if (!entry.LastUsed.HasValue || (now - entry.LastUsed.Value).TotalHours >= 24)
        {
            entry.LastUsed = now;
            Save();
        }
    }

    public void RecordTestResult(VaultEntry entry, TestResult result)
    {
        // A test that never reached the provider (unsupported provider, network error,
        // redirect) says nothing about the key, so it must not mark the entry as failing.
        if (result.CouldNotTest) return;

        entry.LastTest = new EntryTestResult
        {
            Time = DateTimeOffset.UtcNow,
            Success = result.Success,
            StatusCode = result.StatusCode,
            Message = result.Message
        };
        Save();
    }

    public void EnrolDevice(string deviceName, byte[]? customDevicePriv = null)
    {
        // Generate or use device key
        byte[] devPriv = customDevicePriv ?? CryptoPrimitives.GenerateRandomBytes(CryptoConstants.X25519KeySize);
        byte[] devPub = CryptoPrimitives.ComputeX25519PublicKey(devPriv);

        string lockboxId = Guid.NewGuid().ToString("N");
        var (ephPub, nonce, wrappedVaultKey) = CryptoPrimitives.WrapVaultKey(_vaultKey, devPub, Header.VaultId, lockboxId);

        var lockbox = new LockboxModel
        {
            LockboxId = lockboxId,
            Kind = "device",
            RecipientPub = Convert.ToBase64String(devPub),
            EphPub = Convert.ToBase64String(ephPub),
            Nonce = Convert.ToBase64String(nonce),
            WrappedVaultKey = Convert.ToBase64String(wrappedVaultKey)
        };

        var now = DateTimeOffset.UtcNow;
        Header.Lockboxes.Add(lockbox);
        Payload.LockboxRegistry.Add(new LockboxRegistryEntry
        {
            LockboxId = lockboxId,
            Kind = "device",
            Name = deviceName,
            Created = now,
            LastUsed = now
        });

        Save();

        // Pin identity secret and save to OS store
        byte[] idSecret = Convert.FromBase64String(Payload.IdentitySecret);
        _deviceKeyStore.Set(Header.VaultId, new DeviceSecret(lockboxId, devPriv, idSecret));
    }

    public void RemoveDevices(IEnumerable<string> lockboxIdsOrNames)
    {
        var targets = new List<LockboxRegistryEntry>();
        foreach (var idOrName in lockboxIdsOrNames)
        {
            var match = Payload.LockboxRegistry.FirstOrDefault(r =>
                string.Equals(r.LockboxId, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                throw new KeyNotFoundException($"Device '{idOrName}' was not found.");
            }

            if (match.Kind != "device")
            {
                throw new InvalidOperationException($"Cannot remove '{match.Name}' ({match.Kind}): only devices can be removed.");
            }

            if (match.LockboxId == _activeLockboxId)
            {
                throw new InvalidOperationException("You cannot remove the device you are currently using. Remove it from another device.");
            }

            targets.Add(match);
        }

        if (targets.Count == 0) return;

        var targetIds = targets.Select(t => t.LockboxId).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        // Collect padlocks being removed
        foreach (var target in targets)
        {
            var lb = Header.Lockboxes.FirstOrDefault(l => l.LockboxId == target.LockboxId);
            if (lb != null)
            {
                Payload.Revoked.Add(new RevokedLockboxEntry
                {
                    LockboxId = lb.LockboxId,
                    RecipientPub = lb.RecipientPub,
                    RemovedAt = now
                });
            }
        }

        // Discard target lockboxes and registry entries
        Header.Lockboxes.RemoveAll(l => targetIds.Contains(l.LockboxId));
        Payload.LockboxRegistry.RemoveAll(r => targetIds.Contains(r.LockboxId));

        // ROTATE VAULT KEY (Section 6.6)
        RotateVaultKey();
    }

    public void RenameDevice(string newDeviceName)
    {
        var reg = Payload.LockboxRegistry.FirstOrDefault(r => r.LockboxId == _activeLockboxId)
            ?? throw new InvalidOperationException("Current device registry not found.");

        reg.Name = newDeviceName;
        Save();
    }

    public void ChangePassphrase(string newPassphrase, Argon2idParameters? parameters = null)
    {
        var kdfParams = parameters ?? Argon2idParameters.TuneForMachine(fastMode: false);
        byte[] salt = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.SaltSize);
        var derivation = CryptoPrimitives.DerivePassphraseOwner(newPassphrase, salt, kdfParams);

        string newLockboxId = Guid.NewGuid().ToString("N");
        byte[] idSecret = Convert.FromBase64String(Payload.IdentitySecret);
        byte[] idTag = CryptoPrimitives.ComputeIdentityTag(derivation.TagKey!, Header.VaultId, idSecret);

        // Find old passphrase lockbox
        var oldPassphraseLb = Header.Lockboxes.FirstOrDefault(l => l.Kind == "passphrase");
        if (oldPassphraseLb != null)
        {
            Payload.Revoked.Add(new RevokedLockboxEntry
            {
                LockboxId = oldPassphraseLb.LockboxId,
                RecipientPub = oldPassphraseLb.RecipientPub,
                RemovedAt = DateTimeOffset.UtcNow
            });
            Header.Lockboxes.Remove(oldPassphraseLb);
            Payload.LockboxRegistry.RemoveAll(r => r.LockboxId == oldPassphraseLb.LockboxId);
        }

        // Create new passphrase lockbox model placeholder (will be wrapped in RotateVaultKey)
        var newLb = new LockboxModel
        {
            LockboxId = newLockboxId,
            Kind = "passphrase",
            RecipientPub = Convert.ToBase64String(derivation.RecipientPublicKey),
            Kdf = new KdfModel
            {
                Alg = "argon2id",
                Salt = Convert.ToBase64String(salt),
                M = kdfParams.MemoryKiB,
                T = kdfParams.Iterations,
                P = kdfParams.Parallelism
            },
            IdTag = Convert.ToBase64String(idTag)
        };
        Header.Lockboxes.Insert(0, newLb);

        Payload.LockboxRegistry.Insert(0, new LockboxRegistryEntry
        {
            LockboxId = newLockboxId,
            Kind = "passphrase",
            Name = "Passphrase",
            Created = DateTimeOffset.UtcNow,
            LastUsed = DateTimeOffset.UtcNow
        });

        // ROTATE VAULT KEY
        RotateVaultKey();

        // Update local state
        var state = _localStateManager.Load();
        if (state.Vaults.TryGetValue(Header.VaultId, out var vState))
        {
            vState.CurrentPassphraseLockboxId = newLockboxId;
            _localStateManager.Save(state);
        }
    }

    public string RegenerateRecoveryCode()
    {
        string formattedCode = CrockfordBase32.GenerateRecoveryCode();
        byte[] recoveryBytes = CrockfordBase32.Decode(formattedCode);
        var derivation = CryptoPrimitives.DeriveRecoveryOwner(recoveryBytes);

        string newLockboxId = Guid.NewGuid().ToString("N");
        byte[] idSecret = Convert.FromBase64String(Payload.IdentitySecret);
        byte[] idTag = CryptoPrimitives.ComputeIdentityTag(derivation.TagKey!, Header.VaultId, idSecret);

        // Find old recovery lockbox
        var oldRecoveryLb = Header.Lockboxes.FirstOrDefault(l => l.Kind == "recovery");
        if (oldRecoveryLb != null)
        {
            Payload.Revoked.Add(new RevokedLockboxEntry
            {
                LockboxId = oldRecoveryLb.LockboxId,
                RecipientPub = oldRecoveryLb.RecipientPub,
                RemovedAt = DateTimeOffset.UtcNow
            });
            Header.Lockboxes.Remove(oldRecoveryLb);
            Payload.LockboxRegistry.RemoveAll(r => r.LockboxId == oldRecoveryLb.LockboxId);
        }

        var newLb = new LockboxModel
        {
            LockboxId = newLockboxId,
            Kind = "recovery",
            RecipientPub = Convert.ToBase64String(derivation.RecipientPublicKey),
            IdTag = Convert.ToBase64String(idTag)
        };
        Header.Lockboxes.Insert(1, newLb);

        Payload.LockboxRegistry.Insert(1, new LockboxRegistryEntry
        {
            LockboxId = newLockboxId,
            Kind = "recovery",
            Name = "Recovery code",
            Created = DateTimeOffset.UtcNow,
            LastUsed = null
        });

        // ROTATE VAULT KEY
        RotateVaultKey();

        var state = _localStateManager.Load();
        if (state.Vaults.TryGetValue(Header.VaultId, out var vState))
        {
            vState.CurrentRecoveryLockboxId = newLockboxId;
            _localStateManager.Save(state);
        }

        return formattedCode;
    }

    public void AcceptRollback(ulong targetCounter)
    {
        // 1. Drop every revoked padlock
        var revokedPubs = Payload.Revoked.Select(r => r.RecipientPub).ToHashSet();
        Header.Lockboxes.RemoveAll(l => l.Kind == "device" && revokedPubs.Contains(l.RecipientPub));
        Payload.LockboxRegistry.RemoveAll(r => !Header.Lockboxes.Any(l => l.LockboxId == r.LockboxId));

        // 2. Set save counter higher than any seen
        Payload.SaveCounter = targetCounter + 1;

        // 3. Rotate vault key
        RotateVaultKey();
    }

    private void RotateVaultKey()
    {
        // Generate new random 256-bit vault key
        byte[] newVaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);

        // Re-wrap all remaining lockboxes with new vault key
        foreach (var lb in Header.Lockboxes)
        {
            byte[] recipientPub = Convert.FromBase64String(lb.RecipientPub);
            var (ephPub, nonce, wrapped) = CryptoPrimitives.WrapVaultKey(newVaultKey, recipientPub, Header.VaultId, lb.LockboxId);
            lb.EphPub = Convert.ToBase64String(ephPub);
            lb.Nonce = Convert.ToBase64String(nonce);
            lb.WrappedVaultKey = Convert.ToBase64String(wrapped);
        }

        // Replace active vault key
        CryptographicOperations.ZeroMemory(_vaultKey);
        _vaultKey = newVaultKey;

        Save();
    }

    public void Save()
    {
        using var fileLock = VaultFileLock.Acquire(Header.VaultId, TimeSpan.FromSeconds(10));

        Payload.SaveCounter++;

        // Update throttle for last used
        var reg = Payload.LockboxRegistry.FirstOrDefault(r => r.LockboxId == _activeLockboxId);
        if (reg != null)
        {
            reg.LastUsed = DateTimeOffset.UtcNow;
        }

        byte[] serialized = VaultFileFormat.Serialize(Header, Payload, _vaultKey);
        AtomicFileWriter.WriteAtomic(_vaultPath, serialized);

        // Update local state
        var state = _localStateManager.Load();
        if (!state.Vaults.TryGetValue(Header.VaultId, out var vState))
        {
            vState = new VaultLocalState { Path = _vaultPath };
            state.Vaults[Header.VaultId] = vState;
        }

        vState.HighestSaveCounterSeen = Math.Max(vState.HighestSaveCounterSeen, Payload.SaveCounter);
        foreach (var rev in Payload.Revoked)
        {
            if (!vState.RevokedPadlocksSeen.Contains(rev.RecipientPub))
            {
                vState.RevokedPadlocksSeen.Add(rev.RecipientPub);
            }
        }
        _localStateManager.Save(state);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_vaultKey);
            _disposed = true;
        }
    }
}
