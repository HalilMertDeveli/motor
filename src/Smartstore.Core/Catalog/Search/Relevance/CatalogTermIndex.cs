#nullable enable

using System.Text.RegularExpressions;
using Smartstore.Core.Search;

namespace Smartstore.Core.Catalog.Search;

/// <summary>
/// MOTOR: source data of one product for the <see cref="CatalogTermIndex"/>.
/// </summary>
public sealed class CatalogTermDocument
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? ShortDescription { get; init; }

    /// <summary>
    /// SKU, manufacturer part number and GTIN of the product and its attribute combinations.
    /// </summary>
    public IEnumerable<string?>? Codes { get; init; }
    public IEnumerable<string?>? Brands { get; init; }
    public IEnumerable<string?>? Categories { get; init; }
    public IEnumerable<string?>? Tags { get; init; }
}

/// <summary>
/// MOTOR: the products matching a search term, ordered by relevance.
/// </summary>
public sealed class CatalogSearchRanking
{
    /// <summary>
    /// Key of the ranking in <see cref="ISearchQuery.CustomData"/>.
    /// </summary>
    public const string CustomDataKey = "MotorRelevanceRanking";

    public static readonly CatalogSearchRanking Empty = new(string.Empty, [], []);

    /// <summary>
    /// Gets the ranking prepared for the current default term of <paramref name="query"/>, if any.
    /// </summary>
    public static CatalogSearchRanking? FromQuery(ISearchQuery? query)
    {
        if (query?.CustomData != null
            && query.CustomData.TryGetValue(CustomDataKey, out var value)
            && value is CatalogSearchRanking ranking
            && ranking.Term == SearchTextNormalizer.Fold(query.DefaultTerm).Trim())
        {
            return ranking;
        }

        return null;
    }

    public CatalogSearchRanking(string term, int[] productIds, int[][] buckets)
    {
        Term = term;
        ProductIds = productIds;
        Buckets = buckets;
    }

    public string Term { get; }

    /// <summary>
    /// All matching product identifiers, most relevant first.
    /// </summary>
    public int[] ProductIds { get; }

    /// <summary>
    /// The top ranked identifiers grouped by equal relevance (most relevant group first).
    /// Used to sort in the database, where everything after the last bucket keeps the default order.
    /// </summary>
    public int[][] Buckets { get; }
}

/// <summary>
/// MOTOR: an in-memory term index over the catalog that matches and ranks products independently of the
/// database provider. Matching is Turkish-insensitive (see <see cref="SearchTextNormalizer"/>), every
/// significant query word must match a word of the product (whole word, word prefix, a Turkish suffix like
/// "montu" → "Mont", a synonym or a one-letter typo), and products are ranked by where they match:
/// exact code &gt; exact name &gt; name &gt; brand/category &gt; description.
/// </summary>
public sealed class CatalogTermIndex
{
    const int MaxBucketedIds = 500;
    const int MaxBuckets = 24;
    const int MaxFuzzyCandidates = 6;

    private static readonly Regex _codeSeparators = new(@"(?<=[\p{L}\p{N}])[-./](?=[\p{L}\p{N}])", RegexOptions.Compiled);

    // Field weights for exact / Turkish suffix / prefix matches.
    static readonly float[] NameWeights = [40, 34, 26];
    static readonly float[] BrandWeights = [30, 26, 20];
    static readonly float[] CategoryWeights = [24, 22, 16];
    static readonly float[] TagWeights = [18, 16, 12];
    static readonly float[] DescriptionWeights = [8, 7, 5];

    const float SynonymFactor = 0.85f;
    const float FuzzyFactor = 0.6f;
    const float PhraseVariantFactor = 0.9f;

    private readonly Entry[] _entries;
    private readonly string[] _vocabulary;
    private readonly HashSet<string> _vocabularySet;

    private CatalogTermIndex(Entry[] entries)
    {
        _entries = entries;
        _vocabularySet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            _vocabularySet.UnionWith(e.Name);
            _vocabularySet.UnionWith(e.Brands);
            _vocabularySet.UnionWith(e.Categories);
            _vocabularySet.UnionWith(e.Tags);
            _vocabularySet.UnionWith(e.Description);
        }

        _vocabulary = [.. _vocabularySet.Order(StringComparer.Ordinal)];
    }

    public int Count => _entries.Length;

    public static CatalogTermIndex Build(IEnumerable<CatalogTermDocument> documents)
    {
        Guard.NotNull(documents);

        var entries = documents
            .Where(x => x != null)
            .Select(Entry.Create)
            .ToArray();

        return new CatalogTermIndex(entries);
    }

    /// <summary>
    /// Finds and ranks all products matching <paramref name="term"/>.
    /// </summary>
    public CatalogSearchRanking Rank(string? term)
    {
        var folded = SearchTextNormalizer.Fold(term).Trim();
        var tokens = SearchTextNormalizer.Tokenize(folded);
        if (tokens.Length == 0 || _entries.Length == 0)
        {
            return CatalogSearchRanking.Empty;
        }

        var query = new Query(folded, SearchTextNormalizer.Compact(folded), CreateVariants(folded, tokens));
        var scores = ScoreAll(query, allowedMissing: 0);

        if (scores.Count == 0 && query.Variants[0].RequiredCount >= 3)
        {
            // Long queries: tolerate one unmatched word rather than showing nothing.
            scores = ScoreAll(query, allowedMissing: 1);
        }

        var ranked = scores
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .ToArray();

        return new CatalogSearchRanking(folded, [.. ranked.Select(x => x.Id)], CreateBuckets(ranked));
    }

    #region Scoring

    private List<(int Id, float Score, bool Strong)> ScoreAll(Query query, int allowedMissing)
    {
        var result = new List<(int Id, float Score, bool Strong)>();
        var hasStrong = false;

        foreach (var entry in _entries)
        {
            var best = 0f;
            var bestStrong = false;

            foreach (var variant in query.Variants)
            {
                var (score, strong) = ScoreEntry(entry, query, variant, allowedMissing);
                if (score > best)
                {
                    best = score;
                    bestStrong = strong;
                }
            }

            if (best > 0)
            {
                result.Add((entry.Id, best, bestStrong));
                hasStrong |= bestStrong;
            }
        }

        if (hasStrong)
        {
            // Don't flood the result with products that only mention the words in their description.
            result.RemoveAll(x => !x.Strong);
        }

        return result;
    }

    private static (float Score, bool Strong) ScoreEntry(Entry entry, Query query, Variant variant, int allowedMissing)
    {
        var total = 0f;
        var missing = 0;
        var anyStrong = false;
        var allInName = true;
        var lastWordMatched = false;

        foreach (var token in variant.Tokens)
        {
            var best = 0f;
            var strong = false;
            var inName = false;

            foreach (var (alt, weight) in token.Alternatives)
            {
                var m = MatchWord(entry, alt);
                var score = m.Score * weight;
                if (score > best)
                {
                    best = score;
                    strong = m.Strong;
                    inName = m.InName;
                }

                if (!lastWordMatched && !token.Generic && entry.LastNameWord != null && WordMatch(entry.LastNameWord, alt) >= 2)
                {
                    lastWordMatched = true;
                }
            }

            if (token.Generic)
            {
                total += best * 0.5f;
                continue;
            }

            if (best <= 0)
            {
                missing++;
                allInName = false;
                if (missing > allowedMissing)
                {
                    return (0, false);
                }

                continue;
            }

            total += best;
            anyStrong |= strong;
            allInName &= inName;
        }

        if (variant.RequiredCount == 0 || total <= 0)
        {
            return (0, false);
        }

        // Whole query bonuses.
        var compact = query.Compact;
        if (compact.Length >= 3 && entry.Codes.Contains(compact))
        {
            total += 1000;
            anyStrong = true;
        }
        else if (compact.Length >= 4 && HasDigit(compact) && entry.Codes.Any(x => x.StartsWith(compact, StringComparison.Ordinal)))
        {
            total += 300;
            anyStrong = true;
        }

        if (entry.NameJoined == variant.Joined)
        {
            total += 500;
        }
        else if (variant.Tokens.Length > 1 && (" " + entry.NameJoined + " ").Contains(" " + variant.Joined + " ", StringComparison.Ordinal))
        {
            total += 120;
        }

        if (compact.Length >= 4 && HasDigit(compact) && entry.NameCompact.Contains(compact, StringComparison.Ordinal))
        {
            total += 60;
        }

        if (allInName && missing == 0)
        {
            total += 80;
        }

        if (lastWordMatched)
        {
            // Turkish product names end with the head noun: "Kapalı Kask" is a helmet, "Kask Çene Pedi" is not.
            total += 12;
        }

        if (missing > 0)
        {
            total *= 0.5f;
        }

        return (variant.IsPhraseVariant ? total * PhraseVariantFactor : total, anyStrong);
    }

    private static (float Score, bool Strong, bool InName) MatchWord(Entry entry, string word)
    {
        var best = 0f;
        var strong = false;
        var inName = false;

        void Take(float score, bool isStrong, bool isName = false)
        {
            if (score > best)
            {
                best = score;
                strong = isStrong;
                inName = isName;
            }
        }

        var m = WordMatch(entry.Name, word);
        if (m > 0) Take(NameWeights[3 - m], true, true);

        m = WordMatch(entry.Brands, word);
        if (m > 0) Take(BrandWeights[3 - m], true);

        m = WordMatch(entry.Categories, word);
        if (m > 0) Take(CategoryWeights[3 - m], true);

        m = WordMatch(entry.Tags, word);
        if (m > 0) Take(TagWeights[3 - m], true);

        if (best < DescriptionWeights[0])
        {
            m = WordMatch(entry.Description, word);
            if (m > 0) Take(DescriptionWeights[3 - m], false);
        }

        if (word.Length >= 2)
        {
            var hasDigit = HasDigit(word);
            foreach (var code in entry.Codes)
            {
                if (code == word)
                {
                    Take(60, true);
                }
                else if (word.Length >= 3 && hasDigit)
                {
                    if (code.StartsWith(word, StringComparison.Ordinal))
                    {
                        Take(36, true);
                    }
                    else if (code.Contains(word, StringComparison.Ordinal))
                    {
                        Take(22, true);
                    }
                }
            }

            if (word.Length >= 4 && hasDigit && entry.NameCompact.Contains(word, StringComparison.Ordinal))
            {
                // "ff871" → "FF-871"
                Take(30, true, true);
            }
        }

        return (best, strong, inName);
    }

    /// <summary>
    /// 3 = exact word, 2 = word plus Turkish suffix ("montu" → "mont"), 1 = word prefix ("kap" → "kapali"), 0 = no match.
    /// </summary>
    private static int WordMatch(string[] words, string query)
    {
        var best = 0;
        foreach (var w in words)
        {
            var m = WordMatch(w, query);
            if (m > best)
            {
                best = m;
                if (best == 3)
                {
                    break;
                }
            }
        }

        return best;
    }

    private static int WordMatch(string word, string query)
    {
        if (word == query)
        {
            return 3;
        }

        if (IsSuffixed(query, word))
        {
            return 2;
        }

        if (query.Length >= 2 && word.Length > query.Length && word.StartsWith(query, StringComparison.Ordinal))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Whether <paramref name="query"/> is <paramref name="word"/> plus a short letter suffix,
    /// e.g. "eldiveni", "kaski", "montlari".
    /// </summary>
    private static bool IsSuffixed(string query, string word)
    {
        var suffixLength = query.Length - word.Length;
        if (word.Length < 3 || suffixLength < 1 || suffixLength > 4 || !char.IsLetter(word[^1]) || !query.StartsWith(word, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = word.Length; i < query.Length; i++)
        {
            if (!char.IsLetter(query[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDigit(string value)
    {
        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static int[][] CreateBuckets((int Id, float Score, bool Strong)[] ranked)
    {
        var buckets = new List<List<int>>();
        float? lastScore = null;

        foreach (var (id, score, _) in ranked.Take(MaxBucketedIds))
        {
            var rounded = MathF.Round(score);
            if (lastScore != rounded && buckets.Count < MaxBuckets)
            {
                buckets.Add([]);
                lastScore = rounded;
            }

            buckets[^1].Add(id);
        }

        return [.. buckets.Select(x => x.ToArray())];
    }

    #endregion

    #region Query parsing

    private Variant[] CreateVariants(string folded, string[] tokens)
    {
        var variants = new List<Variant> { CreateVariant(tokens, false) };

        // "MT-07", "MT 07", "P2.5", "FF-871" → also try "mt07", "p25", "ff871" as one word.
        var joinedTokens = JoinCodeParts(SearchTextNormalizer.Tokenize(_codeSeparators.Replace(folded, string.Empty)));
        if (!joinedTokens.SequenceEqual(tokens))
        {
            variants.Add(CreateVariant(joinedTokens, true));
        }

        foreach (var (phrase, replacement) in CatalogSearchSynonyms.Phrases)
        {
            var index = IndexOf(tokens, phrase);
            if (index >= 0)
            {
                var replaced = tokens[..index].Concat(replacement).Concat(tokens[(index + phrase.Length)..]).Distinct().ToArray();
                variants.Add(CreateVariant(replaced, true));
            }
        }

        return [.. variants];
    }

    private Variant CreateVariant(string[] words, bool isPhraseVariant)
    {
        var tokens = words
            .Select(w => new QueryToken(w, CatalogSearchSynonyms.GenericWords.Contains(w), GetAlternatives(w)))
            .ToArray();

        if (tokens.All(x => x.Generic))
        {
            // A query of nothing but generic words ("motosiklet") must still match them.
            tokens = [.. tokens.Select(x => x with { Generic = false })];
        }

        return new Variant(tokens, string.Join(' ', words), tokens.Count(x => !x.Generic), isPhraseVariant);
    }

    private List<(string Word, float Weight)> GetAlternatives(string word)
    {
        var alternatives = new List<(string, float)> { (word, 1f) };

        // Also "ceketi" → "ceket" → "mont".
        var synonyms = CatalogSearchSynonyms.Words
            .Where(x => x.Key == word || IsSuffixed(word, x.Key))
            .SelectMany(x => x.Value);

        alternatives.AddRange(synonyms.Select(x => (x, SynonymFactor)));

        if (word.Length >= 4 && !HasDigit(word) && alternatives.All(x => !IsKnown(x.Item1)))
        {
            // Typo tolerance: words of the catalog within one (two for long words) edits.
            var maxDistance = word.Length >= 8 ? 2 : 1;
            var fuzzy = _vocabulary
                .Where(x => x.Length >= 3 && x[0] == word[0] && Math.Abs(x.Length - word.Length) <= maxDistance)
                .Select(x => (Word: x, Distance: EditDistance(word, x, maxDistance)))
                .Where(x => x.Distance <= maxDistance)
                .OrderBy(x => x.Distance)
                .Take(MaxFuzzyCandidates)
                .Select(x => (x.Word, FuzzyFactor));

            alternatives.AddRange(fuzzy);
        }

        return alternatives;
    }

    /// <summary>
    /// Whether any catalog word equals, starts with or is a suffixed form of <paramref name="word"/>.
    /// </summary>
    private bool IsKnown(string word)
    {
        if (_vocabularySet.Contains(word))
        {
            return true;
        }

        var index = Array.BinarySearch(_vocabulary, word, StringComparer.Ordinal);
        index = index < 0 ? ~index : index;
        if (index < _vocabulary.Length && _vocabulary[index].StartsWith(word, StringComparison.Ordinal))
        {
            return true;
        }

        for (var i = 1; i <= 4 && word.Length - i >= 3; i++)
        {
            if (_vocabularySet.Contains(word[..^i]) && IsSuffixed(word, word[..^i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Optimal string alignment distance (Levenshtein plus adjacent transposition), aborting above <paramref name="max"/>.
    /// </summary>
    internal static int EditDistance(string a, string b, int max)
    {
        var prev2 = new int[b.Length + 1];
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, prev2[j - 2] + 1);
                }

                curr[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            (prev2, prev, curr) = (prev, curr, prev2);
        }

        return prev[b.Length];
    }

    /// <summary>
    /// Joins a short letter prefix with the following number: ["mt", "07"] → ["mt07"].
    /// </summary>
    private static string[] JoinCodeParts(string[] tokens)
    {
        var result = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i + 1 < tokens.Length && tokens[i].Length <= 3 && tokens[i].All(char.IsLetter) && tokens[i + 1].All(char.IsDigit))
            {
                result.Add(tokens[i] + tokens[i + 1]);
                i++;
            }
            else
            {
                result.Add(tokens[i]);
            }
        }

        return [.. result];
    }

    private static int IndexOf(string[] tokens, string[] phrase)
    {
        for (var i = 0; i <= tokens.Length - phrase.Length; i++)
        {
            if (tokens.AsSpan(i, phrase.Length).SequenceEqual(phrase))
            {
                return i;
            }
        }

        return -1;
    }

    #endregion

    #region Nested types

    private sealed record Query(string Folded, string Compact, Variant[] Variants);

    private sealed record Variant(QueryToken[] Tokens, string Joined, int RequiredCount, bool IsPhraseVariant);

    private sealed record QueryToken(string Text, bool Generic, List<(string Word, float Weight)> Alternatives);

    private sealed class Entry
    {
        public int Id { get; private init; }
        public string[] Name { get; private init; } = [];
        public string? LastNameWord { get; private init; }
        public string NameJoined { get; private init; } = string.Empty;
        public string NameCompact { get; private init; } = string.Empty;
        public string[] Brands { get; private init; } = [];
        public string[] Categories { get; private init; } = [];
        public string[] Tags { get; private init; } = [];
        public string[] Description { get; private init; } = [];
        public string[] Codes { get; private init; } = [];

        public static Entry Create(CatalogTermDocument doc)
        {
            var name = SearchTextNormalizer.Fold(doc.Name);
            var nameTokens = SearchTextNormalizer.Tokenize(name);
            var description = doc.ShortDescription.HasValue()
                ? SearchTextNormalizer.Tokenize(SearchTextNormalizer.Fold(doc.ShortDescription.RemoveHtml()))
                : [];

            return new Entry
            {
                Id = doc.Id,
                Name = nameTokens,
                LastNameWord = nameTokens.Length > 1 ? nameTokens[^1] : null,
                NameJoined = string.Join(' ', nameTokens),
                NameCompact = SearchTextNormalizer.Compact(name),
                Brands = TokenizeAll(doc.Brands),
                Categories = TokenizeAll(doc.Categories),
                Tags = TokenizeAll(doc.Tags),
                Description = description,
                Codes = [.. (doc.Codes ?? [])
                    .Select(x => SearchTextNormalizer.Compact(SearchTextNormalizer.Fold(x)))
                    .Where(x => x.Length > 0)
                    .Distinct()]
            };
        }

        private static string[] TokenizeAll(IEnumerable<string?>? values)
        {
            if (values == null)
            {
                return [];
            }

            return [.. values.SelectMany(x => SearchTextNormalizer.Tokenize(SearchTextNormalizer.Fold(x))).Distinct()];
        }
    }

    #endregion
}
