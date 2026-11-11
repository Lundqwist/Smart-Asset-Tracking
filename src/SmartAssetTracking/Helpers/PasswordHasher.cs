using System.Security.Cryptography;

namespace SmartAssetTracking.Helpers;

/// <summary>Salted PBKDF2 (SHA-256) password hashing. Format: iterations.salt.hash (Base64).</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
