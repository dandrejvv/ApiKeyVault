using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;

namespace ApiKeyVault.Core.Sync;

public sealed record RollbackCheckResult(
    bool IsRollback,
    bool CounterIsLower,
    bool ContainsRevokedPadlock,
    bool PassphraseIsStale,
    bool RecoveryIsStale,
    ulong FileCounter,
    ulong KnownCounter
);

public static class RollbackChecker
{
    public static RollbackCheckResult Check(
        VaultHeader header,
        VaultPayload payload,
        VaultLocalState? localState)
    {
        if (localState == null)
        {
            return new RollbackCheckResult(false, false, false, false, false, payload.SaveCounter, payload.SaveCounter);
        }

        bool counterIsLower = payload.SaveCounter < localState.HighestSaveCounterSeen;

        // Check if file contains any padlock that was revoked according to our local state
        var filePadlocks = header.Lockboxes.Select(l => l.RecipientPub).ToHashSet(StringComparer.Ordinal);
        bool containsRevokedPadlock = localState.RevokedPadlocksSeen.Any(revokedPub => filePadlocks.Contains(revokedPub));

        // Check if passphrase or recovery lockboxes in file are older/different than the ones we saw
        var currentPassphraseLockbox = header.Lockboxes.FirstOrDefault(l => l.Kind == "passphrase");
        bool passphraseIsStale = localState.CurrentPassphraseLockboxId != null &&
                                currentPassphraseLockbox != null &&
                                currentPassphraseLockbox.LockboxId != localState.CurrentPassphraseLockboxId &&
                                localState.RevokedPadlocksSeen.Contains(currentPassphraseLockbox.RecipientPub);

        var currentRecoveryLockbox = header.Lockboxes.FirstOrDefault(l => l.Kind == "recovery");
        bool recoveryIsStale = localState.CurrentRecoveryLockboxId != null &&
                              currentRecoveryLockbox != null &&
                              currentRecoveryLockbox.LockboxId != localState.CurrentRecoveryLockboxId &&
                              localState.RevokedPadlocksSeen.Contains(currentRecoveryLockbox.RecipientPub);

        bool isRollback = counterIsLower || containsRevokedPadlock;

        return new RollbackCheckResult(
            IsRollback: isRollback,
            CounterIsLower: counterIsLower,
            ContainsRevokedPadlock: containsRevokedPadlock,
            PassphraseIsStale: passphraseIsStale,
            RecoveryIsStale: recoveryIsStale,
            FileCounter: payload.SaveCounter,
            KnownCounter: localState.HighestSaveCounterSeen
        );
    }
}
