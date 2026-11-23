using System.Text;

namespace Bloom.Core.Run;

/// <summary>
/// A run's seed as text. The player sees a short code and can type one, or any other text, to replay a run:
/// the same text always gives the same seed. Capital and small letters are the same, and spaces around the
/// text do not count.
/// </summary>
public static class RunSeed
{
    /// <summary>The longest text the player can type.</summary>
    public const int MaxLength = 16;

    /// <summary>The length of a code made by <see cref="CodeFrom"/>.</summary>
    public const int CodeLength = 8;

    /// <summary>Letters and digits that are hard to mistake for each other: no I, L, O or U.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The text as it is stored and shown: trimmed, in capitals, and cut to <see cref="MaxLength"/>.</summary>
    public static string Normalize(string text)
    {
        string trimmed = text.Trim().ToUpperInvariant();
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }

    /// <summary>The seed for a text (64-bit FNV-1a over the normalized text's UTF-8 bytes).</summary>
    public static ulong FromText(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(Normalize(text)))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    /// <summary>
    /// A code of <see cref="CodeLength"/> characters made from random bits. The caller supplies the bits, since
    /// picking a new run's seed is the one step that is not deterministic.
    /// </summary>
    public static string CodeFrom(ulong randomBits)
    {
        var code = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            code[i] = Alphabet[(int)(randomBits & 31)];
            randomBits >>= 5;
        }

        return new string(code);
    }
}
