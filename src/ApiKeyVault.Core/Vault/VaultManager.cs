using System.Security.Cryptography;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Sync;

namespace ApiKeyVault.Core.Vault;

public sealed record VaultCreationResult(
    VaultSession Session,
    string RecoveryCode
);

public static class VaultManager
{
    public static VaultCreationResult CreateVault(
        string vaultPath,
        string passphrase,
        string? deviceName = null,
        Argon2idParameters? customKdfParams = null,
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? localStateManager = null)
    {
        deviceKeyStore ??= DeviceKeyStoreFactory.CreateDefault();
        localStateManager ??= new LocalStateManager();

        string chosenDeviceName = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName.Trim();
        string vaultId = Guid.NewGuid().ToString("N");
        byte[] vaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);
        byte[] identitySecret = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.IdentitySecretSize);

        // 1. Passphrase owner
        var kdfParams = customKdfParams ?? Argon2idParameters.TuneForMachine(fastMode: false);
        byte[] passSalt = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.SaltSize);
        var passOwner = CryptoPrimitives.DerivePassphraseOwner(passphrase, passSalt, kdfParams);
        string passLockboxId = Guid.NewGuid().ToString("N");
        byte[] passIdTag = CryptoPrimitives.ComputeIdentityTag(passOwner.TagKey!, vaultId, identitySecret);

        var (passEphPub, passNonce, passWrapped) = CryptoPrimitives.WrapVaultKey(
            vaultKey, passOwner.RecipientPublicKey, vaultId, passLockboxId);

        var passLockbox = new LockboxModel
        {
            LockboxId = passLockboxId,
            Kind = "passphrase",
            RecipientPub = Convert.ToBase64String(passOwner.RecipientPublicKey),
            EphPub = Convert.ToBase64String(passEphPub),
            Nonce = Convert.ToBase64String(passNonce),
            WrappedVaultKey = Convert.ToBase64String(passWrapped),
            Kdf = new KdfModel
            {
                Alg = "argon2id",
                Salt = Convert.ToBase64String(passSalt),
                M = kdfParams.MemoryKiB,
                T = kdfParams.Iterations,
                P = kdfParams.Parallelism
            },
            IdTag = Convert.ToBase64String(passIdTag)
        };

        // 2. Recovery owner
        string recoveryCode = CrockfordBase32.GenerateRecoveryCode();
        byte[] recoveryCodeBytes = CrockfordBase32.Decode(recoveryCode);
        var recOwner = CryptoPrimitives.DeriveRecoveryOwner(recoveryCodeBytes);
        string recLockboxId = Guid.NewGuid().ToString("N");
        byte[] recIdTag = CryptoPrimitives.ComputeIdentityTag(recOwner.TagKey!, vaultId, identitySecret);

        var (recEphPub, recNonce, recWrapped) = CryptoPrimitives.WrapVaultKey(
            vaultKey, recOwner.RecipientPublicKey, vaultId, recLockboxId);

        var recLockbox = new LockboxModel
        {
            LockboxId = recLockboxId,
            Kind = "recovery",
            RecipientPub = Convert.ToBase64String(recOwner.RecipientPublicKey),
            EphPub = Convert.ToBase64String(recEphPub),
            Nonce = Convert.ToBase64String(recNonce),
            WrappedVaultKey = Convert.ToBase64String(recWrapped),
            IdTag = Convert.ToBase64String(recIdTag)
        };

        // 3. Device owner
        var devOwner = CryptoPrimitives.GenerateDeviceOwner();
        string devLockboxId = Guid.NewGuid().ToString("N");

        var (devEphPub, devNonce, devWrapped) = CryptoPrimitives.WrapVaultKey(
            vaultKey, devOwner.RecipientPublicKey, vaultId, devLockboxId);

        var devLockbox = new LockboxModel
        {
            LockboxId = devLockboxId,
            Kind = "device",
            RecipientPub = Convert.ToBase64String(devOwner.RecipientPublicKey),
            EphPub = Convert.ToBase64String(devEphPub),
            Nonce = Convert.ToBase64String(devNonce),
            WrappedVaultKey = Convert.ToBase64String(devWrapped)
        };

        var now = DateTimeOffset.UtcNow;
        var header = new VaultHeader
        {
            VaultId = vaultId,
            Lockboxes = [passLockbox, recLockbox, devLockbox]
        };

        var payload = new VaultPayload
        {
            IdentitySecret = Convert.ToBase64String(identitySecret),
            SaveCounter = 1,
            LockboxRegistry =
            [
                new LockboxRegistryEntry { LockboxId = passLockboxId, Kind = "passphrase", Name = "Passphrase", Created = now, LastUsed = now },
                new LockboxRegistryEntry { LockboxId = recLockboxId, Kind = "recovery", Name = "Recovery code", Created = now, LastUsed = null },
                new LockboxRegistryEntry { LockboxId = devLockboxId, Kind = "device", Name = chosenDeviceName, Created = now, LastUsed = now }
            ],
            Revoked = [],
            Entries = [],
            Profiles = [],
            Settings = [],
            Tombstones = []
        };

        // Write file atomically
        byte[] fileBytes = VaultFileFormat.Serialize(header, payload, vaultKey);
        AtomicFileWriter.WriteAtomic(vaultPath, fileBytes);

        // Save device credentials to OS store
        deviceKeyStore.Set(vaultId, new DeviceSecret(devLockboxId, devOwner.RecipientPrivateKey, identitySecret));

        // Save local state
        var state = localStateManager.Load();
        state.Vaults[vaultId] = new VaultLocalState
        {
            Path = Path.GetFullPath(vaultPath),
            HighestSaveCounterSeen = 1,
            CurrentPassphraseLockboxId = passLockboxId,
            CurrentRecoveryLockboxId = recLockboxId,
            RevokedPadlocksSeen = []
        };
        state.Settings.DefaultVaultPath = Path.GetFullPath(vaultPath);
        localStateManager.Save(state);

        var session = new VaultSession(
            vaultPath: Path.GetFullPath(vaultPath),
            vaultKey: vaultKey,
            header: header,
            payload: payload,
            activeLockboxId: devLockboxId,
            isDeviceEnrolled: true,
            deviceKeyStore: deviceKeyStore,
            localStateManager: localStateManager);

        return new VaultCreationResult(session, recoveryCode);
    }

    public static VaultSession OpenWithDevice(
        string vaultPath,
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? localStateManager = null,
        bool acceptRollback = false)
    {
        deviceKeyStore ??= DeviceKeyStoreFactory.CreateDefault();
        localStateManager ??= new LocalStateManager();

        if (!File.Exists(vaultPath))
        {
            throw new VaultNotFoundException(vaultPath);
        }

        byte[] fileBytes = File.ReadAllBytes(vaultPath);
        var parsed = VaultFileFormat.ParseContainer(fileBytes);

        var deviceSecret = deviceKeyStore.Get(parsed.Header.VaultId)
            ?? throw new VaultAccessDeniedException("This device is not enrolled for this vault.");

        var lockbox = parsed.Header.Lockboxes.FirstOrDefault(l => l.LockboxId == deviceSecret.LockboxId && l.Kind == "device");
        if (lockbox == null)
        {
            throw new VaultAccessDeniedException("This device no longer has access to this vault.");
        }

        byte[] vaultKey = CryptoPrimitives.UnwrapVaultKey(
            wrappedVaultKey: Convert.FromBase64String(lockbox.WrappedVaultKey),
            nonce: Convert.FromBase64String(lockbox.Nonce),
            ephPub: Convert.FromBase64String(lockbox.EphPub),
            recipientPub: Convert.FromBase64String(lockbox.RecipientPub),
            recipientPriv: deviceSecret.DevicePrivateKey,
            vaultId: parsed.Header.VaultId,
            lockboxId: lockbox.LockboxId);

        var payload = VaultFileFormat.UnsealPayload(parsed, vaultKey);

        // Verify pinned identity secret
        byte[] payloadIdentitySecret = Convert.FromBase64String(payload.IdentitySecret);
        if (!CryptographicOperations.FixedTimeEquals(payloadIdentitySecret, deviceSecret.IdentitySecret))
        {
            throw new VaultTamperedException("Identity secret mismatch: this vault is not the vault this device knows.");
        }

        return FinalizeOpen(
            vaultPath, vaultKey, parsed.Header, payload, deviceSecret.LockboxId,
            isDeviceEnrolled: true, deviceKeyStore, localStateManager, acceptRollback);
    }

    public static VaultSession OpenWithPassphrase(
        string vaultPath,
        string passphrase,
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? localStateManager = null,
        bool acceptRollback = false)
    {
        deviceKeyStore ??= DeviceKeyStoreFactory.CreateDefault();
        localStateManager ??= new LocalStateManager();

        if (!File.Exists(vaultPath))
        {
            throw new VaultNotFoundException(vaultPath);
        }

        byte[] fileBytes = File.ReadAllBytes(vaultPath);
        var parsed = VaultFileFormat.ParseContainer(fileBytes);

        var lockbox = parsed.Header.Lockboxes.FirstOrDefault(l => l.Kind == "passphrase")
            ?? throw new VaultTamperedException("No passphrase lockbox found in vault.");

        if (lockbox.Kdf == null)
        {
            throw new VaultTamperedException("Passphrase lockbox missing KDF parameters.");
        }

        var kdfParams = new Argon2idParameters
        {
            MemoryKiB = lockbox.Kdf.M,
            Iterations = lockbox.Kdf.T,
            Parallelism = lockbox.Kdf.P
        };
        kdfParams.Validate(); // Strict bounds check before deriving!

        byte[] salt = Convert.FromBase64String(lockbox.Kdf.Salt);
        var passOwner = CryptoPrimitives.DerivePassphraseOwner(passphrase, salt, kdfParams);

        byte[] vaultKey;
        try
        {
            vaultKey = CryptoPrimitives.UnwrapVaultKey(
                wrappedVaultKey: Convert.FromBase64String(lockbox.WrappedVaultKey),
                nonce: Convert.FromBase64String(lockbox.Nonce),
                ephPub: Convert.FromBase64String(lockbox.EphPub),
                recipientPub: Convert.FromBase64String(lockbox.RecipientPub),
                recipientPriv: passOwner.RecipientPrivateKey,
                vaultId: parsed.Header.VaultId,
                lockboxId: lockbox.LockboxId);
        }
        catch (Exception ex)
        {
            throw new VaultAccessDeniedException("Passphrase didn't open this vault.", ex);
        }

        var payload = VaultFileFormat.UnsealPayload(parsed, vaultKey);

        // Verify Identity Tag (Crypto §7.2)
        if (string.IsNullOrEmpty(lockbox.IdTag))
        {
            throw new VaultTamperedException("Passphrase lockbox missing identity tag.");
        }

        byte[] idSecret = Convert.FromBase64String(payload.IdentitySecret);
        byte[] expectedTag = Convert.FromBase64String(lockbox.IdTag);
        if (!CryptoPrimitives.VerifyIdentityTag(passOwner.TagKey!, parsed.Header.VaultId, idSecret, expectedTag))
        {
            throw new VaultTamperedException("Identity tag mismatch. This vault is a look-alike.");
        }

        // Check if this machine is already enrolled
        var devSecret = deviceKeyStore.Get(parsed.Header.VaultId);
        bool isEnrolled = devSecret != null && parsed.Header.Lockboxes.Any(l => l.LockboxId == devSecret.LockboxId);

        return FinalizeOpen(
            vaultPath, vaultKey, parsed.Header, payload, lockbox.LockboxId,
            isDeviceEnrolled: isEnrolled, deviceKeyStore, localStateManager, acceptRollback);
    }

    public static VaultSession OpenWithRecovery(
        string vaultPath,
        string recoveryCode,
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? localStateManager = null,
        bool acceptRollback = false)
    {
        deviceKeyStore ??= DeviceKeyStoreFactory.CreateDefault();
        localStateManager ??= new LocalStateManager();

        if (!File.Exists(vaultPath))
        {
            throw new VaultNotFoundException(vaultPath);
        }

        byte[] recoveryBytes = CrockfordBase32.Decode(recoveryCode);
        var recOwner = CryptoPrimitives.DeriveRecoveryOwner(recoveryBytes);

        byte[] fileBytes = File.ReadAllBytes(vaultPath);
        var parsed = VaultFileFormat.ParseContainer(fileBytes);

        var lockbox = parsed.Header.Lockboxes.FirstOrDefault(l => l.Kind == "recovery")
            ?? throw new VaultTamperedException("No recovery lockbox found in vault.");

        byte[] vaultKey;
        try
        {
            vaultKey = CryptoPrimitives.UnwrapVaultKey(
                wrappedVaultKey: Convert.FromBase64String(lockbox.WrappedVaultKey),
                nonce: Convert.FromBase64String(lockbox.Nonce),
                ephPub: Convert.FromBase64String(lockbox.EphPub),
                recipientPub: Convert.FromBase64String(lockbox.RecipientPub),
                recipientPriv: recOwner.RecipientPrivateKey,
                vaultId: parsed.Header.VaultId,
                lockboxId: lockbox.LockboxId);
        }
        catch (Exception ex)
        {
            throw new VaultAccessDeniedException("Recovery code didn't open this vault.", ex);
        }

        var payload = VaultFileFormat.UnsealPayload(parsed, vaultKey);

        if (string.IsNullOrEmpty(lockbox.IdTag))
        {
            throw new VaultTamperedException("Recovery lockbox missing identity tag.");
        }

        byte[] idSecret = Convert.FromBase64String(payload.IdentitySecret);
        byte[] expectedTag = Convert.FromBase64String(lockbox.IdTag);
        if (!CryptoPrimitives.VerifyIdentityTag(recOwner.TagKey!, parsed.Header.VaultId, idSecret, expectedTag))
        {
            throw new VaultTamperedException("Identity tag mismatch. This vault is a look-alike.");
        }

        var devSecret = deviceKeyStore.Get(parsed.Header.VaultId);
        bool isEnrolled = devSecret != null && parsed.Header.Lockboxes.Any(l => l.LockboxId == devSecret.LockboxId);

        return FinalizeOpen(
            vaultPath, vaultKey, parsed.Header, payload, lockbox.LockboxId,
            isDeviceEnrolled: isEnrolled, deviceKeyStore, localStateManager, acceptRollback);
    }

    private static VaultSession FinalizeOpen(
        string vaultPath,
        byte[] vaultKey,
        VaultHeader header,
        VaultPayload payload,
        string activeLockboxId,
        bool isDeviceEnrolled,
        IDeviceKeyStore deviceKeyStore,
        LocalStateManager localStateManager,
        bool acceptRollback)
    {
        string fullVaultPath = Path.GetFullPath(vaultPath);
        string dir = Path.GetDirectoryName(fullVaultPath) ?? "";

        // 1. Check for conflict copies in same directory (Section 9.3)
        var conflictFiles = Directory.GetFiles(dir, "vault-*.akv");
        if (conflictFiles.Length > 0)
        {
            var candidates = new List<MergeCandidate>
            {
                new(fullVaultPath, header, payload)
            };

            foreach (var cf in conflictFiles)
            {
                try
                {
                    byte[] cfBytes = File.ReadAllBytes(cf);
                    var cfParsed = VaultFileFormat.ParseContainer(cfBytes);
                    if (cfParsed.Header.VaultId == header.VaultId)
                    {
                        var cfPayload = VaultFileFormat.UnsealPayload(cfParsed, vaultKey);
                        if (cfPayload.IdentitySecret == payload.IdentitySecret)
                        {
                            candidates.Add(new MergeCandidate(cf, cfParsed.Header, cfPayload));
                        }
                    }
                }
                catch
                {
                    // If conflict file fails verification, don't merge it (leaves it alone)
                }
            }

            if (candidates.Count > 1)
            {
                var mergeResult = ConflictMerger.Merge(candidates);
                header = mergeResult.Header;
                payload = mergeResult.Payload;

                // Archive conflict copies to conflicts/ directory
                string conflictsDir = Path.Combine(dir, "conflicts");
                Directory.CreateDirectory(conflictsDir);
                foreach (var cf in conflictFiles)
                {
                    try
                    {
                        string dest = Path.Combine(conflictsDir, Path.GetFileName(cf));
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(cf, dest);
                    }
                    catch { /* ignore cleanup error */ }
                }
            }
        }

        // 2. Rollback check (Section 7.3)
        var state = localStateManager.Load();
        state.Vaults.TryGetValue(header.VaultId, out var localVaultState);

        var rollback = RollbackChecker.Check(header, payload, localVaultState);
        if (rollback.IsRollback)
        {
            if (!acceptRollback)
            {
                throw new VaultRollbackException(rollback.FileCounter, rollback.KnownCounter, rollback.ContainsRevokedPadlock);
            }
        }

        // 3. Update local state
        if (localVaultState == null)
        {
            localVaultState = new VaultLocalState { Path = fullVaultPath };
            state.Vaults[header.VaultId] = localVaultState;
        }

        localVaultState.HighestSaveCounterSeen = Math.Max(localVaultState.HighestSaveCounterSeen, payload.SaveCounter);
        foreach (var rev in payload.Revoked)
        {
            if (!localVaultState.RevokedPadlocksSeen.Contains(rev.RecipientPub))
            {
                localVaultState.RevokedPadlocksSeen.Add(rev.RecipientPub);
            }
        }

        var passLb = header.Lockboxes.FirstOrDefault(l => l.Kind == "passphrase");
        if (passLb != null) localVaultState.CurrentPassphraseLockboxId = passLb.LockboxId;

        var recLb = header.Lockboxes.FirstOrDefault(l => l.Kind == "recovery");
        if (recLb != null) localVaultState.CurrentRecoveryLockboxId = recLb.LockboxId;

        localStateManager.Save(state);

        return new VaultSession(
            vaultPath: fullVaultPath,
            vaultKey: vaultKey,
            header: header,
            payload: payload,
            activeLockboxId: activeLockboxId,
            isDeviceEnrolled: isDeviceEnrolled,
            deviceKeyStore: deviceKeyStore,
            localStateManager: localStateManager);
    }
}
