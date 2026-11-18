using System.Globalization;
using System.Text;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Cleans a value up according to what its column holds (<see cref="FieldKind"/>) before it's vectorized, so the
/// same thing written two ways gives the same trigrams: Bob and Robert, Acme Corp. and ACME Corporation,
/// 3/4/1980 and 1980-03-04, (555) 010-1234 and 555.010.1234.
///
/// The output feeds the vectors, so any change to these rules or tables makes datasets cleaned by an earlier
/// version unmatchable with new ones: give it a new <see cref="Version"/> rather than changing it silently.
/// </summary>
public static class FieldCleaner
{
    /// <summary>The version of these rules, recorded with every dataset (and vectors workbook) that used them.</summary>
    public const string Version = "clean-1";

    /// <summary>The versions this build can produce (and so match against).</summary>
    public static bool IsKnownVersion(string? version) => string.IsNullOrEmpty(version) || version == Version;

    public static string Clean(FieldKind kind, string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        return kind switch
        {
            FieldKind.Name => CleanName(text),
            FieldKind.Company => CleanCompany(text),
            FieldKind.Address => CleanAddress(text),
            FieldKind.DateMonthFirst => CleanDate(text, monthFirst: true),
            FieldKind.DateDayFirst => CleanDate(text, monthFirst: false),
            FieldKind.Phone => CleanPhone(text),
            FieldKind.PostalCode => CleanPostalCode(text),
            FieldKind.Code => LettersAndDigits(text),
            _ => text
        };
    }

    // ── Names ─────────────────────────────────────────────────────────────

    /// <summary>"Smith, Robert J." and "Mr Bob J Smith" both become "robert j smith".</summary>
    private static string CleanName(string text)
    {
        // "Last, First" (one comma) is turned round to "First Last".
        var parts = text.Split(',');
        if (parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0
            && !NameSuffixes.Contains(Words(parts[1]).FirstOrDefault() ?? ""))
            text = parts[1] + " " + parts[0];

        var words = Words(text)
            .Where(w => !NameTitles.Contains(w) && !NameSuffixes.Contains(w))
            .Select(w => Nicknames.TryGetValue(w, out var formal) ? formal : w);
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> NameTitles = new(StringComparer.Ordinal)
    {
        "mr", "mrs", "ms", "miss", "mx", "dr", "prof", "rev", "sir", "dame", "fr"
    };

    private static readonly HashSet<string> NameSuffixes = new(StringComparer.Ordinal)
    {
        "jr", "sr", "ii", "iii", "iv", "md", "phd", "dds", "esq", "rn", "dmd"
    };

    /// <summary>
    /// Common English nicknames and their formal names. Names that are short for more than one name (Chris, Alex,
    /// Sam, Pat, Terry, Jo...) are left out, since mapping them would turn a woman's name into a man's or the reverse.
    /// </summary>
    private static readonly Dictionary<string, string> Nicknames = BuildNicknames(
        ("robert", "bob bobby rob robbie bert"),
        ("william", "bill billy will willie liam"),
        ("richard", "rick ricky dick rich richie"),
        ("james", "jim jimmy jamie"),
        ("john", "jack johnny jon"),
        ("joseph", "joe joey"),
        ("michael", "mike mikey mick"),
        ("thomas", "tom tommy"),
        ("charles", "charlie chuck chas"),
        ("edward", "ed eddie ted teddy ned"),
        ("anthony", "tony"),
        ("daniel", "dan danny"),
        ("david", "dave davey"),
        ("steven", "steve stevie stephen"),
        ("matthew", "matt matty"),
        ("andrew", "andy drew"),
        ("joshua", "josh"),
        ("kenneth", "ken kenny"),
        ("benjamin", "ben benny"),
        ("nicholas", "nick nicky"),
        ("jonathan", "jonny"),
        ("timothy", "tim timmy"),
        ("gregory", "greg"),
        ("raymond", "ray"),
        ("lawrence", "larry"),
        ("ronald", "ron ronnie"),
        ("donald", "don donnie"),
        ("gerald", "jerry gerry"),
        ("harold", "harry hal"),
        ("henry", "hank"),
        ("walter", "walt wally"),
        ("frederick", "fred freddie"),
        ("leonard", "len lenny leo"),
        ("douglas", "doug"),
        ("peter", "pete"),
        ("patrick", "paddy"),
        ("philip", "phil"),
        ("phillip", "phil"),
        ("samuel", "sammy"),
        ("zachary", "zach zack"),
        ("vincent", "vince vinny"),
        ("jeffrey", "jeff"),
        ("geoffrey", "geoff"),
        ("bradley", "brad"),
        ("theodore", "theo"),
        ("eugene", "gene"),
        ("elizabeth", "liz lizzie beth betty betsy eliza libby"),
        ("margaret", "maggie meg peggy marge margie"),
        ("katherine", "kate kathy katie kat kitty"),
        ("catherine", "cathy cate"),
        ("jennifer", "jen jenny"),
        ("patricia", "patty trish tricia"),
        ("susan", "sue susie"),
        ("deborah", "debbie deb debra"),
        ("rebecca", "becky becca"),
        ("barbara", "barb babs"),
        ("dorothy", "dot dottie"),
        ("victoria", "vicky tori"),
        ("christina", "tina chrissy"),
        ("kimberly", "kim"),
        ("pamela", "pam"),
        ("cynthia", "cindy"),
        ("abigail", "abby"),
        ("amanda", "mandy"),
        ("samantha", "sammie"),
        ("jessica", "jessie"),
        ("rosemary", "rosie"),
        ("theresa", "terri tess"),
        ("judith", "judy"),
        ("melissa", "missy mel"),
        ("valerie", "val"),
        ("eleanor", "ellie nell"),
        ("caroline", "carrie"),
        ("josephine", "josie"),
        ("veronica", "ronnie"));

    private static Dictionary<string, string> BuildNicknames(params (string Formal, string Short)[] names)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (formal, shorts) in names)
            foreach (var nickname in shorts.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                map.TryAdd(nickname, formal);   // first listed wins (ronnie: ronald)
        return map;
    }

    // ── Companies ─────────────────────────────────────────────────────────

    /// <summary>"The Acme Corp., Inc." and "ACME Corporation" both become "acme".</summary>
    private static string CleanCompany(string text)
    {
        var words = Words(text.Replace("&", " and ")).ToList();
        if (words.Count > 1 && words[0] == "the")
            words.RemoveAt(0);
        // Legal forms come off the end, one after another (Acme Holdings Co Ltd keeps Holdings), but never the
        // whole name.
        while (words.Count > 1 && CompanySuffixes.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "corp", "corporation", "co", "company", "llc", "lc", "ltd", "limited", "plc",
        "lp", "llp", "lllp", "pllc", "pc", "pa", "gmbh", "ag", "sa", "sas", "srl", "bv", "nv", "pty", "oy", "ab", "as"
    };

    // ── Addresses ─────────────────────────────────────────────────────────

    /// <summary>"123 North Main Street, Apartment 4" becomes "123 n main st apt 4" (USPS abbreviations).</summary>
    private static string CleanAddress(string text)
    {
        var words = Words(text.Replace("#", " apt ")).ToList();
        var result = new List<string>(words.Count);
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            // P.O. Box, PO Box and Post Office Box.
            if ((word is "po" && i + 1 < words.Count && words[i + 1] == "box")
                || (word == "post" && i + 2 < words.Count && words[i + 1] == "office" && words[i + 2] == "box"))
            {
                result.Add("pobox");
                i += word == "po" ? 1 : 2;
                continue;
            }
            if (word == "p" && i + 2 < words.Count && words[i + 1] == "o" && words[i + 2] == "box")
            {
                result.Add("pobox");
                i += 2;
                continue;
            }
            result.Add(AddressWords.TryGetValue(word, out var shortForm) ? shortForm : word);
        }
        // "apt apt 4" from "# Apt 4" or "Apt #4".
        for (int i = result.Count - 1; i > 0; i--)
            if (result[i] == result[i - 1] && result[i] is "apt" or "ste")
                result.RemoveAt(i);
        return string.Join(' ', result);
    }

    private static readonly Dictionary<string, string> AddressWords = new(StringComparer.Ordinal)
    {
        ["street"] = "st", ["str"] = "st", ["avenue"] = "ave", ["av"] = "ave", ["road"] = "rd",
        ["boulevard"] = "blvd", ["drive"] = "dr", ["lane"] = "ln", ["court"] = "ct", ["place"] = "pl",
        ["highway"] = "hwy", ["parkway"] = "pkwy", ["circle"] = "cir", ["terrace"] = "ter", ["square"] = "sq",
        ["trail"] = "trl", ["way"] = "way", ["expressway"] = "expy", ["freeway"] = "fwy", ["turnpike"] = "tpke",
        ["crossing"] = "xing", ["point"] = "pt", ["mount"] = "mt", ["fort"] = "ft", ["route"] = "rte",
        ["apartment"] = "apt", ["suite"] = "ste", ["building"] = "bldg", ["floor"] = "fl", ["unit"] = "unit",
        ["room"] = "rm", ["department"] = "dept",
        ["north"] = "n", ["south"] = "s", ["east"] = "e", ["west"] = "w",
        ["northeast"] = "ne", ["northwest"] = "nw", ["southeast"] = "se", ["southwest"] = "sw",
        ["first"] = "1st", ["second"] = "2nd", ["third"] = "3rd", ["fourth"] = "4th", ["fifth"] = "5th",
        ["sixth"] = "6th", ["seventh"] = "7th", ["eighth"] = "8th", ["ninth"] = "9th", ["tenth"] = "10th"
    };

    // ── Dates ─────────────────────────────────────────────────────────────

    private static readonly string[] MonthFirstFormats =
    {
        "M/d/yyyy", "M-d-yyyy", "M.d.yyyy", "M/d/yy", "M-d-yy", "M.d.yy", "MMddyyyy",
        "M/d/yyyy H:mm", "M/d/yyyy h:mm tt", "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm:ss tt"
    };

    private static readonly string[] DayFirstFormats =
    {
        "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy", "d.M.yy", "ddMMyyyy",
        "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss"
    };

    private static readonly string[] UnambiguousFormats =
    {
        "yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "yyyyMMdd", "yyyy-M-d H:mm:ss", "yyyy-M-dTH:mm:ss", "yyyy-M-d H:mm",
        "MMMM d yyyy", "MMM d yyyy", "d MMMM yyyy", "d MMM yyyy", "MMMM d, yyyy", "MMM d, yyyy", "dddd, MMMM d, yyyy",
        "d-MMM-yyyy", "d-MMM-yy", "MMM-d-yyyy"
    };

    /// <summary>Any common way of writing a date becomes yyyy-MM-dd; a value that isn't a date is kept as it is.</summary>
    private static string CleanDate(string text, bool monthFirst)
    {
        var compact = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var formats = (monthFirst ? MonthFirstFormats : DayFirstFormats).Concat(UnambiguousFormats).ToArray();
        if (DateTime.TryParseExact(compact, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A date cell read without its format comes through as Excel's serial number (days since 1899-12-30).
        if (double.TryParse(compact, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is >= 1 and < 2_958_466 && !compact.Contains('.') && compact.Length <= 6)
            return DateTime.FromOADate(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return string.Join(' ', Words(text));
    }

    // ── Phones, postal codes, codes ───────────────────────────────────────

    /// <summary>Digits only, without an extension or a leading US country code: +1 (555) 010-1234 x12 becomes 5550101234.</summary>
    private static string CleanPhone(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var marker in new[] { "ext", "x", "#" })
        {
            int at = lower.IndexOf(marker, StringComparison.Ordinal);
            if (at > 0 && lower[..at].Count(char.IsDigit) >= 7)
            {
                lower = lower[..at];
                break;
            }
        }
        var digits = new string(lower.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1')
            digits = digits[1..];
        return digits;
    }

    /// <summary>A US ZIP+4 is cut to its five digits (12345-6789 is 12345); anything else keeps its letters and digits.</summary>
    private static string CleanPostalCode(string text)
    {
        var code = LettersAndDigits(text);
        if (code.Length == 9 && code.All(char.IsAsciiDigit))
            return code[..5];
        // A ZIP that lost its leading zero in Excel (2134 for 02134).
        if (code.Length == 4 && code.All(char.IsAsciiDigit))
            return "0" + code;
        return code;
    }

    private static string LettersAndDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>A value's words: lower case, accents dropped, split on anything that isn't a letter or digit.</summary>
    private static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (ch == '\'' || ch == '’')   // O'Neil and ONeil are the same word
                continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }
}
