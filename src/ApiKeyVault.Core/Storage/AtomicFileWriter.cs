using System.Security.Cryptography;

namespace ApiKeyVault.Core.Storage;

public sealed class VaultFileLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;
    private bool _disposed;

    private VaultFileLock(Mutex mutex, bool hasHandle)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
    }

    public static VaultFileLock Acquire(string vaultId, TimeSpan timeout)
    {
        // Sanitize vaultId for mutex name
        string cleanId = new string(vaultId.Where(char.IsLetterOrDigit).ToArray());
        string mutexName = $"Local\\ApiKeyVault_{cleanId}";

        var mutex = new Mutex(false, mutexName);
        bool hasHandle = false;
        try
        {
            hasHandle = mutex.WaitOne(timeout);
            if (!hasHandle)
            {
                throw new TimeoutException($"Could not acquire lock for vault {vaultId} within {timeout.TotalSeconds} seconds.");
            }
            return new VaultFileLock(mutex, true);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_hasHandle)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore if thread already released
                }
                _hasHandle = false;
            }
            _mutex.Dispose();
            _disposed = true;
        }
    }
}

public static class AtomicFileWriter
{
    public static byte[] ComputeFileHash(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    public static void WriteAtomic(string targetPath, byte[] content, byte[]? expectedTargetHash = null, int maxRetries = 10)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new ArgumentException("Invalid target file path.", nameof(targetPath));

        Directory.CreateDirectory(dir);

        string tempPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.tmp.{Guid.NewGuid():N}");

        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(content, 0, content.Length);
                fs.Flush(flushToDisk: true);
            }

            // Retry loop for file replacement (handles OneDrive transient sharing locks)
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    if (File.Exists(targetPath))
                    {
                        if (expectedTargetHash != null)
                        {
                            byte[] currentHash = ComputeFileHash(targetPath);
                            if (!currentHash.AsSpan().SequenceEqual(expectedTargetHash))
                            {
                                throw new IOException("Target file was modified on disk while preparing write.");
                            }
                        }

                        // Atomic replace
                        File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }
                    break;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup error */ }
            }
        }
    }
}
