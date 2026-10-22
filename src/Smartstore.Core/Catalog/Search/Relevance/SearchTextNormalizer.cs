#nullable enable

using System.Net;
using System.Text;

namespace Smartstore.Core.Catalog.Search;

/// <summary>
/// MOTOR: culture-safe text folding for catalog search. Folds Turkish letters to their ASCII base
/// (ı/İ/I → i, ş → s, ğ → g, ü → u, ö → o, ç → c) and lowercases everything else, so that
/// "KAPALI", "kapalı" and "kapali" compare equal regardless of the server culture.
/// </summary>
public static class SearchTextNormalizer
{
    /// <summary>
    /// Folds <paramref name="text"/> to lowercase ASCII-like text. The result has the same length as the input
    /// (one char per char, combining dots are folded to a space), which keeps positions usable for highlighting.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = FoldChar(src[i]);
            }
        });
    }

    public static char FoldChar(char c)
    {
        switch (c)
        {
            case 'I':
            case 'İ':
            case 'ı':
            case 'Î':
            case 'î':
                return 'i';
            case 'Ş':
            case 'ş':
                return 's';
            case 'Ğ':
            case 'ğ':
                return 'g';
            case 'Ü':
            case 'ü':
            case 'Û':
            case 'û':
                return 'u';
            case 'Ö':
            case 'ö':
                return 'o';
            case 'Ç':
            case 'ç':
                return 'c';
            case 'Â':
            case 'â':
                return 'a';
            case '̇': // Combining dot above ("i̇" from a culture-unaware ToLower of "İ").
                return ' ';
            default:
                return char.ToLowerInvariant(c);
        }
    }

    /// <summary>
    /// Splits folded text into distinct word tokens (letters and digits only).
    /// "FF-871 Kapalı Kask" → ["ff", "871", "kapali", "kask"].
    /// </summary>
    public static string[] Tokenize(string? foldedText)
    {
        if (string.IsNullOrEmpty(foldedText))
        {
            return [];
        }

        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i <= foldedText.Length; i++)
        {
            var isWordChar = i < foldedText.Length && char.IsLetterOrDigit(foldedText[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                var token = foldedText[start..i];
                if (!tokens.Contains(token))
                {
                    tokens.Add(token);
                }

                start = -1;
            }
        }

        return [.. tokens];
    }

    /// <summary>
    /// Removes everything but letters and digits: "FF-871 / P2.5" → "ff871p25".
    /// Used to compare SKUs and part numbers regardless of punctuation.
    /// </summary>
    public static string Compact(string? foldedText)
    {
        if (string.IsNullOrEmpty(foldedText))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(foldedText.Length);
        foreach (var c in foldedText)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a value indicating whether <paramref name="text"/> matches every word of <paramref name="term"/>
    /// (folded, word-prefix match). "Kapalı Kask" matches "kapali", "KASK" and "kap kas".
    /// </summary>
    public static bool MatchesAllWords(string? text, string? term)
    {
        var termTokens = Tokenize(Fold(term));
        if (termTokens.Length == 0)
        {
            return false;
        }

        var textTokens = Tokenize(Fold(text));
        return termTokens.All(t => textTokens.Any(x => x.StartsWith(t, StringComparison.Ordinal)));
    }

    /// <summary>
    /// HTML-encodes <paramref name="text"/> and wraps every word that starts with a word of
    /// <paramref name="term"/> (folded comparison) into <paramref name="preMatch"/>/<paramref name="postMatch"/>.
    /// Safe to output with Html.Raw because all text parts are encoded.
    /// </summary>
    public static string HighlightHtml(string? text, string? term, string preMatch = "<mark>", string postMatch = "</mark>")
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var termTokens = Tokenize(Fold(term));
        if (termTokens.Length == 0)
        {
            return WebUtility.HtmlEncode(text);
        }

        var folded = Fold(text);
        var sb = new StringBuilder(text.Length + 32);
        var pos = 0;
        var start = -1;

        for (var i = 0; i <= folded.Length; i++)
        {
            var isWordChar = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                var word = folded[start..i];
                var match = termTokens
                    .Where(t => word.StartsWith(t, StringComparison.Ordinal))
                    .MaxBy(t => t.Length);

                if (match != null)
                {
                    sb.Append(WebUtility.HtmlEncode(text[pos..start]));
                    sb.Append(preMatch);
                    sb.Append(WebUtility.HtmlEncode(text.Substring(start, match.Length)));
                    sb.Append(postMatch);
                    pos = start + match.Length;
                }

                start = -1;
            }
        }

        sb.Append(WebUtility.HtmlEncode(text[pos..]));
        return sb.ToString();
    }
}
