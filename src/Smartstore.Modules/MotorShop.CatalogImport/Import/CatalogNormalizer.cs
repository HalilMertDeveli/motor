using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorShop.CatalogImport.Import;

/// <summary>
/// Canonical names and comparison keys. Display values (part numbers above all) are never rewritten;
/// keys exist only to detect duplicates and to match fitment statements to motorcycles.
/// </summary>
public static partial class CatalogNormalizer
{
    private static readonly Dictionary<string, string> _brandAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hiflo"] = "Hiflofiltro",
        ["hiflo filtro"] = "Hiflofiltro",
        ["hiflofiltro"] = "Hiflofiltro",
        ["ebc"] = "EBC Brakes",
        ["ebc brakes"] = "EBC Brakes",
        ["ngk"] = "NGK",
        ["ngk spark plugs"] = "NGK",
        ["niterra"] = "NGK",
        ["denso"] = "DENSO",
        ["did"] = "D.I.D",
        ["d.i.d"] = "D.I.D",
        ["d.i.d."] = "D.I.D",
        ["daido kogyo"] = "D.I.D",
        ["rk"] = "RK",
        ["rk chain"] = "RK",
        ["rk excel"] = "RK",
        ["jt"] = "JT Sprockets",
        ["jt sprockets"] = "JT Sprockets",
        ["brembo"] = "Brembo",
        ["yuasa"] = "Yuasa",
        ["k&n"] = "K&N",
        ["k & n"] = "K&N",
        ["k and n"] = "K&N"
    };

    private static readonly Dictionary<string, string> _makeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yamaha motor"] = "Yamaha",
        ["honda motor"] = "Honda",
        ["kawasaki motors"] = "Kawasaki",
        ["suzuki motor"] = "Suzuki",
        ["ktm sportmotorcycle"] = "KTM",
        ["bajaj auto"] = "Bajaj",
        ["tvs motor"] = "TVS",
        ["tvs motor company"] = "TVS",
        ["cf moto"] = "CFMOTO",
        ["cfmoto"] = "CFMOTO",
        ["cf-moto"] = "CFMOTO",
        ["bmw"] = "BMW Motorrad",
        ["bmw motorrad"] = "BMW Motorrad"
    };

    /// <summary>
    /// Product types MOTOR stages. Aliases include common English and Turkish spellings.
    /// </summary>
    private static readonly Dictionary<string, string> _productTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oil-filter"] = "oil-filter",
        ["oil filter"] = "oil-filter",
        ["yağ filtresi"] = "oil-filter",
        ["air-filter"] = "air-filter",
        ["air filter"] = "air-filter",
        ["hava filtresi"] = "air-filter",
        ["brake-pad"] = "brake-pad",
        ["brake pad"] = "brake-pad",
        ["brake pads"] = "brake-pad",
        ["fren balatası"] = "brake-pad",
        ["brake-disc"] = "brake-disc",
        ["brake disc"] = "brake-disc",
        ["fren diski"] = "brake-disc",
        ["chain"] = "chain",
        ["zincir"] = "chain",
        ["sprocket"] = "sprocket",
        ["dişli"] = "sprocket",
        ["spark-plug"] = "spark-plug",
        ["spark plug"] = "spark-plug",
        ["buji"] = "spark-plug",
        ["battery"] = "battery",
        ["akü"] = "battery",
        ["clutch"] = "clutch",
        ["debriyaj"] = "clutch",
        ["other"] = "other"
    };

    public static IReadOnlyCollection<string> ProductTypes { get; } = [.. _productTypes.Values.Distinct()];

    public static string CollapseWhitespace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : WhitespaceRegex().Replace(value.Trim(), " ");

    public static string Brand(string value)
    {
        value = CollapseWhitespace(value);
        return value != null && _brandAliases.TryGetValue(value, out var canonical) ? canonical : value;
    }

    public static string Make(string value)
    {
        value = CollapseWhitespace(value);
        return value != null && _makeAliases.TryGetValue(value, out var canonical) ? canonical : value;
    }

    /// <summary>
    /// Returns the canonical product type or null if the type is not supported.
    /// </summary>
    public static string ProductType(string value)
    {
        value = CollapseWhitespace(value);
        return value != null && _productTypes.TryGetValue(value, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// Lower-case letters and digits only: "EBC Brakes" -> "ebcbrakes", "D.I.D" -> "did".
    /// </summary>
    public static string Key(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : NonAlphanumericRegex().Replace(value, string.Empty).ToLowerInvariant();

    /// <summary>
    /// Upper-case MPN without spaces, hyphens, dots and slashes: "HF-204" -> "HF204". Only for duplicate detection.
    /// </summary>
    public static string MpnKey(string mpn)
        => string.IsNullOrWhiteSpace(mpn) ? string.Empty : MpnSeparatorRegex().Replace(mpn, string.Empty).ToUpperInvariant();

    /// <summary>
    /// Model comparison key: "YZF-R25", "YZF R25" and "yzf-r25" are equal; "NC750X" and "NC750S" are not.
    /// </summary>
    public static string ModelKey(string model)
        => Key(model).ToUpperInvariant();

    public static string MakeKey(string make)
        => Key(Make(make)).ToUpperInvariant();

    public static string Currency(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"[\s\-\./]+")]
    private static partial Regex MpnSeparatorRegex();
}
