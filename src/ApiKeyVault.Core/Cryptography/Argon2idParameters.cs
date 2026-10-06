using System.Diagnostics;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace ApiKeyVault.Core.Cryptography;

public record Argon2idParameters
{
    public int MemoryKiB { get; init; } = CryptoConstants.DefaultMemoryKiB;
    public int Iterations { get; init; } = CryptoConstants.DefaultIterations;
    public int Parallelism { get; init; } = CryptoConstants.DegreeOfParallelism;

    public void Validate()
    {
        if (MemoryKiB < CryptoConstants.MinMemoryKiB || MemoryKiB > CryptoConstants.MaxMemoryKiB)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MemoryKiB),
                $"Argon2id memory {MemoryKiB} KiB is out of safe bounds ({CryptoConstants.MinMemoryKiB} - {CryptoConstants.MaxMemoryKiB} KiB).");
        }

        if (Iterations < CryptoConstants.MinIterations || Iterations > CryptoConstants.MaxIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Iterations),
                $"Argon2id iterations {Iterations} is out of safe bounds ({CryptoConstants.MinIterations} - {CryptoConstants.MaxIterations}).");
        }

        if (Parallelism != CryptoConstants.DegreeOfParallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Parallelism),
                $"Argon2id degree of parallelism must be {CryptoConstants.DegreeOfParallelism}, but got {Parallelism}.");
        }
    }

    /// <summary>
    /// Tunes iterations starting from default (3) up to max iterations, aiming for target 500-1000ms.
    /// In fast/test mode, returns default directly without benchmarking.
    /// </summary>
    public static Argon2idParameters TuneForMachine(bool fastMode = false)
    {
        if (fastMode)
        {
            return new Argon2idParameters
            {
                MemoryKiB = CryptoConstants.DefaultMemoryKiB,
                Iterations = CryptoConstants.DefaultIterations,
                Parallelism = CryptoConstants.DegreeOfParallelism
            };
        }

        // Test with 3 iterations
        int currentIterations = CryptoConstants.DefaultIterations;
        var testParams = new NSec.Cryptography.Argon2Parameters
        {
            MemorySize = CryptoConstants.DefaultMemoryKiB,
            NumberOfPasses = currentIterations,
            DegreeOfParallelism = CryptoConstants.DegreeOfParallelism
        };

        var algo = PasswordBasedKeyDerivationAlgorithm.Argon2id(in testParams);
        byte[] dummySalt = new byte[CryptoConstants.SaltSize];
        RandomNumberGenerator.Fill(dummySalt);

        var sw = Stopwatch.StartNew();
        algo.DeriveBytes("benchmark_passphrase", dummySalt, 64);
        sw.Stop();

        long elapsedMs = sw.ElapsedMilliseconds;

        // If it took less than 400ms, scale iterations proportionally up to 64
        if (elapsedMs > 0 && elapsedMs < 400)
        {
            double ratio = 500.0 / elapsedMs;
            int suggested = (int)Math.Round(currentIterations * ratio);
            currentIterations = Math.Clamp(suggested, CryptoConstants.MinIterations, CryptoConstants.MaxIterations);
        }

        return new Argon2idParameters
        {
            MemoryKiB = CryptoConstants.DefaultMemoryKiB,
            Iterations = currentIterations,
            Parallelism = CryptoConstants.DegreeOfParallelism
        };
    }
}
