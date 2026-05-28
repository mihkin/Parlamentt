using System.Security.Cryptography;

namespace ParlamB.Api.Services;

public sealed class PasswordHashService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public (byte[] Hash, byte[] Salt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (hash, salt);
    }

    public bool VerifyPassword(string password, byte[] expectedHash, byte[] salt)
    {
        if (string.IsNullOrWhiteSpace(password) || expectedHash.Length == 0 || salt.Length == 0)
            return false;

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
