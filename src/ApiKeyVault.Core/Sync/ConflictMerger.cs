using ApiKeyVault.Core.Model;

namespace ApiKeyVault.Core.Sync;

public sealed record MergeCandidate(
    string FilePath,
    VaultHeader Header,
    VaultPayload Payload
);

public sealed record MergeResult(
    VaultHeader Header,
    VaultPayload Payload,
    List<string> MergedFilePaths,
    List<string> AttentionItems
);

public static class ConflictMerger
{
    public static MergeResult Merge(IReadOnlyList<MergeCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one candidate is required for merge.", nameof(candidates));
        }

        if (candidates.Count == 1)
        {
            return new MergeResult(
                candidates[0].Header,
                candidates[0].Payload,
                [],
                []
            );
        }

        var attentionItems = new List<string>();
        var mergedPaths = new List<string>();
        string vaultId = candidates[0].Header.VaultId;
        string identitySecret = candidates[0].Payload.IdentitySecret;

        // Verify all candidates belong to same vault and have same identity secret
        foreach (var c in candidates)
        {
            if (c.Header.VaultId != vaultId || c.Payload.IdentitySecret != identitySecret)
            {
                throw new VaultTamperedException($"Candidate file '{c.FilePath}' does not match vault id or identity secret.");
            }
            mergedPaths.Add(c.FilePath);
        }

        // 1. Merge Revoked padlocks
        var allRevoked = new Dictionary<string, RevokedLockboxEntry>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            foreach (var rev in c.Payload.Revoked)
            {
                if (!allRevoked.TryGetValue(rev.RecipientPub, out var existing) || rev.RemovedAt > existing.RemovedAt)
                {
                    allRevoked[rev.RecipientPub] = rev;
                }
            }
        }

        // 2. Merge Lockboxes
        // Find newest passphrase lockbox
        var passphraseLockboxes = candidates
            .SelectMany(c => c.Header.Lockboxes.Where(l => l.Kind == "passphrase"))
            .GroupBy(l => l.LockboxId)
            .Select(g => g.First())
            .ToList();

        if (passphraseLockboxes.Count == 0)
        {
            throw new VaultTamperedException("No passphrase lockbox found in candidate vaults.");
        }

        LockboxModel winningPassphrase = passphraseLockboxes[0];
        if (passphraseLockboxes.Count > 1)
        {
            // Pick newest registry entry or first
            winningPassphrase = passphraseLockboxes.OrderByDescending(p => p.LockboxId).First();
            foreach (var p in passphraseLockboxes)
            {
                if (p.LockboxId != winningPassphrase.LockboxId)
                {
                    allRevoked[p.RecipientPub] = new RevokedLockboxEntry
                    {
                        LockboxId = p.LockboxId,
                        RecipientPub = p.RecipientPub,
                        RemovedAt = DateTimeOffset.UtcNow
                    };
                }
            }
        }

        // Find newest recovery lockbox
        var recoveryLockboxes = candidates
            .SelectMany(c => c.Header.Lockboxes.Where(l => l.Kind == "recovery"))
            .GroupBy(l => l.LockboxId)
            .Select(g => g.First())
            .ToList();

        if (recoveryLockboxes.Count == 0)
        {
            throw new VaultTamperedException("No recovery lockbox found in candidate vaults.");
        }

        LockboxModel winningRecovery = recoveryLockboxes[0];
        if (recoveryLockboxes.Count > 1)
        {
            winningRecovery = recoveryLockboxes.OrderByDescending(r => r.LockboxId).First();
            foreach (var r in recoveryLockboxes)
            {
                if (r.LockboxId != winningRecovery.LockboxId)
                {
                    allRevoked[r.RecipientPub] = new RevokedLockboxEntry
                    {
                        LockboxId = r.LockboxId,
                        RecipientPub = r.RecipientPub,
                        RemovedAt = DateTimeOffset.UtcNow
                    };
                }
            }
        }

        // Surviving device lockboxes
        var survivingLockboxes = new List<LockboxModel> { winningPassphrase, winningRecovery };
        var deviceLockboxes = candidates
            .SelectMany(c => c.Header.Lockboxes.Where(l => l.Kind == "device"))
            .GroupBy(l => l.RecipientPub)
            .Select(g => g.First())
            .Where(l => !allRevoked.ContainsKey(l.RecipientPub))
            .ToList();

        survivingLockboxes.AddRange(deviceLockboxes);

        // Lockbox registry
        var lockboxRegistry = new Dictionary<string, LockboxRegistryEntry>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            foreach (var reg in c.Payload.LockboxRegistry)
            {
                if (survivingLockboxes.Any(l => l.LockboxId == reg.LockboxId))
                {
                    if (!lockboxRegistry.TryGetValue(reg.LockboxId, out var existing))
                    {
                        lockboxRegistry[reg.LockboxId] = reg;
                    }
                    else
                    {
                        // keep latest last_used
                        if (reg.LastUsed.HasValue && (!existing.LastUsed.HasValue || reg.LastUsed.Value > existing.LastUsed.Value))
                        {
                            existing.LastUsed = reg.LastUsed;
                        }
                    }
                }
            }
        }

        // 3. Tombstones
        var allTombstones = new Dictionary<string, Tombstone>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            foreach (var t in c.Payload.Tombstones)
            {
                if (!allTombstones.TryGetValue(t.Id, out var existing) || t.Stamp.CompareTo(existing.Stamp) > 0)
                {
                    allTombstones[t.Id] = t;
                }
            }
        }

        // 4. Entries
        var mergedEntries = new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
        var allEntries = candidates.SelectMany(c => c.Payload.Entries).ToList();

        foreach (var entry in allEntries)
        {
            // Check tombstone
            if (allTombstones.TryGetValue(entry.Id, out var ts) && ts.Stamp.CompareTo(entry.Stamp) >= 0)
            {
                continue; // Deleted
            }

            if (!mergedEntries.TryGetValue(entry.Id, out var existing))
            {
                mergedEntries[entry.Id] = entry;
            }
            else
            {
                // Merge existing with entry
                // Details stamp winner
                VaultEntry detailsWinner = existing.Stamp.CompareTo(entry.Stamp) >= 0 ? existing : entry;
                VaultEntry detailsLoser = detailsWinner == existing ? entry : existing;

                // Secret stamp winner
                VaultEntry secretWinner = existing.SecretStamp.CompareTo(entry.SecretStamp) >= 0 ? existing : entry;
                VaultEntry secretLoser = secretWinner == existing ? entry : existing;

                string finalSecret = secretWinner.Secret;
                Stamp finalSecretStamp = secretWinner.SecretStamp;
                string? finalPrevSecret = secretWinner.PreviousSecret;
                DateTimeOffset? finalPrevUntil = secretWinner.PreviousUntil;

                // Check secret collision: if both rotated
                if (existing.Secret != entry.Secret)
                {
                    if (secretLoser.Secret != finalSecret && secretLoser.Secret != finalPrevSecret)
                    {
                        finalPrevSecret = secretLoser.Secret;
                        finalPrevUntil = DateTimeOffset.UtcNow.AddDays(7);
                        attentionItems.Add($"Conflict on key '{detailsWinner.Provider}/{detailsWinner.Name}': both devices changed the secret. Kept newest secret and preserved previous secret.");
                    }
                }

                // Latest usage
                DateTimeOffset? latestUsed = existing.LastUsed.HasValue && entry.LastUsed.HasValue
                    ? (existing.LastUsed.Value > entry.LastUsed.Value ? existing.LastUsed : entry.LastUsed)
                    : (existing.LastUsed ?? entry.LastUsed);

                EntryTestResult? latestTest = existing.LastTest;
                if (entry.LastTest != null)
                {
                    if (latestTest == null || entry.LastTest.Time > latestTest.Time)
                    {
                        latestTest = entry.LastTest;
                    }
                }

                var merged = new VaultEntry
                {
                    Id = detailsWinner.Id,
                    Provider = detailsWinner.Provider,
                    Name = detailsWinner.Name,
                    Comment = detailsWinner.Comment,
                    Source = detailsWinner.Source,
                    Expires = detailsWinner.Expires,
                    ReviewBy = detailsWinner.ReviewBy,
                    ExtraFields = detailsWinner.ExtraFields,
                    IsCompromised = detailsWinner.IsCompromised || detailsLoser.IsCompromised,
                    IsRevoked = detailsWinner.IsRevoked,
                    Tags = (detailsWinner.Tags?.Count > 0 ? detailsWinner.Tags : detailsLoser.Tags) ?? [],
                    Stamp = detailsWinner.Stamp,
                    Secret = finalSecret,
                    SecretStamp = finalSecretStamp,
                    PreviousSecret = finalPrevSecret,
                    PreviousUntil = finalPrevUntil,
                    Created = existing.Created < entry.Created ? existing.Created : entry.Created,
                    LastUsed = latestUsed,
                    LastTest = latestTest
                };

                mergedEntries[entry.Id] = merged;
            }
        }

        // 5. Profiles
        var mergedProfiles = new Dictionary<string, VaultProfile>(StringComparer.Ordinal);
        foreach (var prof in candidates.SelectMany(c => c.Payload.Profiles))
        {
            if (allTombstones.TryGetValue(prof.Id, out var ts) && ts.Stamp.CompareTo(prof.Stamp) >= 0)
            {
                continue;
            }

            if (!mergedProfiles.TryGetValue(prof.Id, out var existing) || prof.Stamp.CompareTo(existing.Stamp) > 0)
            {
                mergedProfiles[prof.Id] = prof;
            }
        }

        // 6. Settings
        var mergedSettings = new Dictionary<string, VaultSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            foreach (var (key, val) in c.Payload.Settings)
            {
                if (!mergedSettings.TryGetValue(key, out var existing) || val.Stamp.CompareTo(existing.Stamp) > 0)
                {
                    mergedSettings[key] = val;
                }
            }
        }

        // 7. Save counter
        ulong maxCounter = candidates.Max(c => c.Payload.SaveCounter);

        var mergedHeader = new VaultHeader
        {
            VaultId = vaultId,
            Lockboxes = survivingLockboxes
        };

        var mergedPayload = new VaultPayload
        {
            IdentitySecret = identitySecret,
            SaveCounter = maxCounter + 1,
            LockboxRegistry = lockboxRegistry.Values.ToList(),
            Revoked = allRevoked.Values.ToList(),
            Entries = mergedEntries.Values.ToList(),
            Profiles = mergedProfiles.Values.ToList(),
            Settings = mergedSettings,
            Tombstones = allTombstones.Values.ToList()
        };

        return new MergeResult(mergedHeader, mergedPayload, mergedPaths, attentionItems);
    }
}
