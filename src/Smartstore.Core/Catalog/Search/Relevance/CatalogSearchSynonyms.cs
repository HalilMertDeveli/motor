#nullable enable

namespace Smartstore.Core.Catalog.Search;

/// <summary>
/// MOTOR: a deliberately small synonym list for storefront search. Keys and values are folded
/// (see <see cref="SearchTextNormalizer.Fold(string?)"/>). Only add entries that map a common customer
/// word to the word the catalog actually uses, e.g. customers say "ceket", product names say "Mont".
/// </summary>
public static class CatalogSearchSynonyms
{
    /// <summary>
    /// Single word → alternative words. A product matching an alternative ranks slightly below a direct match.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Words = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ceket"] = ["mont"],
        ["jacket"] = ["mont"],
        ["helmet"] = ["kask"],
        ["glove"] = ["eldiven"],
        ["gloves"] = ["eldiven"],
        ["bag"] = ["canta"],
        ["boot"] = ["bot"],
        ["boots"] = ["bot"],
        ["intercom"] = ["interkom"],
        ["pantalon"] = ["pantolon"],
        ["jet"] = ["acik"],
        ["fullface"] = ["kapali"],
        ["visor"] = ["vizor"],
        ["topcase"] = ["arka"],
        ["motor"] = ["motosiklet"],
        ["moto"] = ["motosiklet"]
    };

    /// <summary>
    /// Word sequences that customers type for a catalog term, e.g. "full face kask" → "kapali kask".
    /// </summary>
    public static readonly IReadOnlyList<(string[] Phrase, string[] Replacement)> Phrases =
    [
        (["full", "face"], ["kapali"]),
        (["tam", "kapali"], ["kapali"]),
        (["flip", "up"], ["cene", "acilir"]),
        (["moduler"], ["cene", "acilir"]),
        (["modular"], ["cene", "acilir"]),
        (["yarim", "kask"], ["acik", "kask"]),
        (["top", "case"], ["arka", "canta"])
    ];

    /// <summary>
    /// Words that describe nearly every product in a motorcycle store. They are optional unless the query
    /// consists of nothing else, so "motosiklet montu" also finds "4 Mevsim Mont".
    /// </summary>
    public static readonly IReadOnlySet<string> GenericWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "motor", "moto", "motosiklet", "motorsiklet", "motosikleti", "icin", "ile", "ve", "en", "urun", "urunu", "urunleri",
        "model", "modeli", "fiyat", "fiyati", "fiyatlari", "ucuz", "yeni", "orijinal", "the", "for", "and"
    };
}
