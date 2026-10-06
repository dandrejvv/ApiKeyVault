using ApiKeyVault.Core.Cryptography;

namespace ApiKeyVault.Core.Tests;

public class Argon2idParametersTests
{
    [Fact]
    public void DefaultParameters_AreWithinValidBounds()
    {
        var parameters = new Argon2idParameters();
        parameters.Validate();
        Assert.Equal(CryptoConstants.DefaultMemoryKiB, parameters.MemoryKiB);
        Assert.Equal(CryptoConstants.DefaultIterations, parameters.Iterations);
        Assert.Equal(1, parameters.Parallelism);
    }

    [Theory]
    [InlineData(10 * 1024)] // 10 MiB - below min 19 MiB
    [InlineData(300 * 1024)] // 300 MiB - above max 256 MiB
    public void Memory_OutOfBounds_Throws(int memoryKiB)
    {
        var parameters = new Argon2idParameters { MemoryKiB = memoryKiB };
        Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());
    }

    [Theory]
    [InlineData(1)] // below min 2
    [InlineData(65)] // above max 64
    public void Iterations_OutOfBounds_Throws(int iterations)
    {
        var parameters = new Argon2idParameters { Iterations = iterations };
        Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());
    }

    [Fact]
    public void Parallelism_NotOne_Throws()
    {
        var parameters = new Argon2idParameters { Parallelism = 2 };
        Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());
    }

    [Fact]
    public void FastMode_ReturnsValidParameters()
    {
        var parameters = Argon2idParameters.TuneForMachine(fastMode: true);
        parameters.Validate();
        Assert.Equal(CryptoConstants.DefaultMemoryKiB, parameters.MemoryKiB);
        Assert.Equal(CryptoConstants.DefaultIterations, parameters.Iterations);
    }
}
