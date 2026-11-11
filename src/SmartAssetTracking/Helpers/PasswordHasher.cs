// ============================================================================
// PasswordHasher.cs
// ----------------------------------------------------------------------------
// HOW PASSWORDS ARE STORED SAFELY:
//
// We never save the actual password. Instead we save a "hash": the result of
// running the password through a one-way mathematical function. You can't
// turn the hash back into the password. To check a login, we hash what the
// user typed and compare it with the stored hash.
//
// Two extra protections:
//   * SALT: a random value mixed in before hashing. Two users with the same
//     password ("1234") get completely different hashes, so an attacker can't
//     use pre-computed tables of common passwords.
//   * ITERATIONS: the hash is repeated 100,000 times (PBKDF2). That is fast
//     enough for one login, but makes guessing millions of passwords very slow.
//
// The stored text looks like:  100000.<salt in Base64>.<hash in Base64>
// ============================================================================

using System.Security.Cryptography;

namespace SmartAssetTracking.Helpers;

/// <summary>Salted PBKDF2 (SHA-256) password hashing. Format: iterations.salt.hash (Base64).</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;      // bytes of random salt
    private const int KeySize = 32;       // bytes in the resulting hash
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    // Turns a password into a storable hash string.
    public static string Hash(string password)
    {
        // Throws an error if the password is null or empty.
        ArgumentException.ThrowIfNullOrEmpty(password);

        // A new random salt for every password.
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        // Base64 turns raw bytes into normal text characters that can be saved in the database.
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    // Returns true if the password matches the stored hash.
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        // Split "100000.salt.hash" into its three parts.
        var parts = storedHash.Split('.');

        // int.TryParse returns false (instead of crashing) if the text is not a number.
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);

            // Hash the typed password with the SAME salt and iteration count...
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

            // ...and compare. FixedTimeEquals always takes the same time, so an attacker
            // can't learn anything by measuring how fast a wrong guess is rejected.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // The stored value was not valid Base64, so treat it as a failed login.
            return false;
        }
    }
}
