using System.Text;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Lawrence Philips' Metaphone (1990): a word's consonant sounds as a short code, so names spelled differently but
/// said alike get the same code. Smith and Smyth are both "SM0" (0 is "th"), John and Jon "JN", Catherine and Kathryn
/// "K0RN", Stephen and Steven "STFN". Letters only; accents are dropped first.
/// </summary>
public static class Metaphone
{
    public static string Encode(string word)
    {
        var w = Letters(word);
        if (w.Length == 0)
            return string.Empty;

        // Adjacent duplicate letters count once, except C.
        var dedup = new StringBuilder(w.Length);
        for (int i = 0; i < w.Length; i++)
            if (i == 0 || w[i] != w[i - 1] || w[i] == 'C')
                dedup.Append(w[i]);
        w = dedup.ToString();

        // Silent first letters, and the initial X and WH.
        if (w.Length > 1 && (w.StartsWith("KN") || w.StartsWith("GN") || w.StartsWith("PN") || w.StartsWith("AE") || w.StartsWith("WR")))
            w = w[1..];
        if (w[0] == 'X')
            w = "S" + w[1..];
        else if (w.StartsWith("WH"))
            w = "W" + w[2..];

        var code = new StringBuilder(w.Length);
        int n = w.Length;
        for (int i = 0; i < n; i++)
        {
            char c = w[i];
            char prev = i > 0 ? w[i - 1] : '\0';
            char next = i + 1 < n ? w[i + 1] : '\0';
            char next2 = i + 2 < n ? w[i + 2] : '\0';
            switch (c)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    if (i == 0) code.Append(c);
                    break;
                case 'B':
                    if (!(prev == 'M' && i == n - 1)) code.Append('B');   // "dumb"
                    break;
                case 'C':
                    if (next == 'I' && next2 == 'A') code.Append('X');
                    else if (next == 'H') { code.Append(prev == 'S' ? 'K' : 'X'); i++; }
                    else if (next is 'I' or 'E' or 'Y') code.Append('S');
                    else if (next == 'K') { }                                 // CK: the K speaks
                    else code.Append('K');
                    break;
                case 'D':
                    if (next == 'G' && next2 is 'E' or 'I' or 'Y') { code.Append('J'); i++; }
                    else code.Append('T');
                    break;
                case 'G':
                    if (next == 'H' && i + 2 < n && !IsVowel(next2)) { }      // "night"
                    else if (next == 'N' && (i + 2 == n || w[(i + 1)..] == "NED")) { }   // "sign", "signed"
                    else if (next is 'I' or 'E' or 'Y') code.Append('J');
                    else code.Append('K');
                    break;
                case 'H':
                    if (prev is 'C' or 'S' or 'P' or 'T' or 'G') { }
                    else if (IsVowel(prev) && !IsVowel(next)) { }
                    else code.Append('H');
                    break;
                case 'P':
                    if (next == 'H') { code.Append('F'); i++; }
                    else code.Append('P');
                    break;
                case 'Q':
                    code.Append('K');
                    break;
                case 'S':
                    if (next == 'H') { code.Append('X'); i++; }
                    else if (next == 'I' && next2 is 'O' or 'A') code.Append('X');
                    else code.Append('S');
                    break;
                case 'T':
                    if (next == 'I' && next2 is 'A' or 'O') code.Append('X');
                    else if (next == 'H') { code.Append('0'); i++; }
                    else if (next == 'C' && next2 == 'H') { }
                    else code.Append('T');
                    break;
                case 'V':
                    code.Append('F');
                    break;
                case 'W' or 'Y':
                    if (IsVowel(next)) code.Append(c);
                    break;
                case 'X':
                    code.Append("KS");
                    break;
                case 'Z':
                    code.Append('S');
                    break;
                default:   // F J K L M N R
                    code.Append(c);
                    break;
            }
        }
        return code.ToString();
    }

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';

    private static string Letters(string word)
    {
        if (string.IsNullOrEmpty(word))
            return string.Empty;
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.Normalize(NormalizationForm.FormD))
            if (char.IsAsciiLetter(ch))
                sb.Append(char.ToUpperInvariant(ch));
            else if (ch == 'ß')
                sb.Append("SS");
        return sb.ToString();
    }
}
