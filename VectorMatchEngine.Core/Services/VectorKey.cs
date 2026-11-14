using System.Security.Cryptography;
using System.Text;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// A matching key: the secret both sides of a match vectorize with. Without it the vectors can't be rebuilt from
/// guesses, so a dictionary attack (vectorize likely names and DOBs, compare) fails. The passphrase is stretched with
/// PBKDF2 so guessing weak passphrases is slow, and only a short <see cref="Fingerprint"/> of it is ever stored,
/// so two datasets can be checked for the same key without keeping the key.
/// </summary>
public sealed class VectorKey
{
    /// <summary>Fixed, so the same passphrase gives the same key on every machine (both sides must agree).</summary>
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("VectorMatchEngine matching key v1");
    private const int Iterations = 600_000;

    internal byte[] Bytes { get; }

    /// <summary>A short check value for the key, such as "3F9A-12C4-9B0E"; it doesn't reveal the key.</summary>
    public string Fingerprint { get; }

    private VectorKey(byte[] bytes)
    {
        Bytes = bytes;
        var check = HMACSHA256.HashData(bytes, Encoding.UTF8.GetBytes("VectorMatchEngine key fingerprint"));
        var hex = Convert.ToHexString(check, 0, 6);
        Fingerprint = $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}";
    }

    /// <summary>The key for a passphrase; null for a blank one (no key). Takes a fraction of a second, by design.</summary>
    public static VectorKey? FromPassphrase(string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            return null;
        return new VectorKey(Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC)), Salt, Iterations, HashAlgorithmName.SHA256, 32));
    }
}
