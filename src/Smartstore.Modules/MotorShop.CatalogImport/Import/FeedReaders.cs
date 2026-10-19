using System.Globalization;
using System.Text;
using System.Text.Json;
using Smartstore;

namespace MotorShop.CatalogImport.Import;

public class FeedFormatException(string message) : Exception(message);

/// <summary>
/// Parses research datasets (JSON arrays) and supplier/parts CSV files into records.
/// Readers never validate content; they only reject files that cannot be parsed at all.
/// </summary>
public static class FeedReaders
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool IsJson(string fileName, string content)
        => (fileName?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ?? false)
        || content.TrimStart().StartsWith('[');

    public static List<FeedRow<T>> ReadJson<T>(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FeedFormatException("The JSON root must be an array of records.");
            }

            var rows = new List<FeedRow<T>>();
            var row = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                row++;
                rows.Add(new FeedRow<T>(row, element.Deserialize<T>(_jsonOptions)));
            }

            return rows;
        }
        catch (JsonException ex)
        {
            // The message names line and position, never content.
            throw new FeedFormatException($"Invalid JSON: line {ex.LineNumber + 1}, position {ex.BytePositionInLine}.");
        }
    }

    /// <summary>
    /// Parts CSV (comma separated). Columns: brand, product_name, product_type, mpn, superseded_by, oem_references,
    /// gtin, classification, package_quantity, specs, fitment, source_url, source_organization, source_type,
    /// accessed, license_status, verification_status, confidence, notes.
    /// <c>oem_references</c> and <c>specs</c> ("key=value") are '|' separated; <c>fitment</c> entries are '|' separated
    /// with fields "Make;Model;YearFrom;YearTo;Position" and use <c>source_url</c> as their evidence.
    /// </summary>
    public static List<FeedRow<PartRecord>> ReadPartsCsv(string content)
    {
        var result = new List<FeedRow<PartRecord>>();

        foreach (var (row, get) in ReadCsv(content, ','))
        {
            var sourceUrl = get("source_url");
            var record = new PartRecord
            {
                Brand = get("brand"),
                ProductName = get("product_name"),
                ProductType = get("product_type"),
                Mpn = get("mpn"),
                SupersededBy = get("superseded_by"),
                OemReferences = SplitList(get("oem_references")),
                Gtin = get("gtin"),
                Classification = get("classification"),
                PackageQuantity = ParseInt(get("package_quantity")),
                Specs = SplitList(get("specs"))
                    .Select(x => x.Split('=', 2))
                    .Where(x => x.Length == 2)
                    .GroupBy(x => x[0].Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First()[1].Trim(), StringComparer.OrdinalIgnoreCase),
                LicenseStatus = get("license_status"),
                VerificationStatus = get("verification_status"),
                Confidence = get("confidence"),
                Notes = get("notes"),
                RetrievalMethod = "csv feed"
            };

            foreach (var entry in SplitList(get("fitment")))
            {
                var f = entry.Split(';').Select(x => x.Trim()).ToArray();
                record.Fitment.Add(new FitmentRecord
                {
                    Make = f.ElementAtOrDefault(0),
                    Model = f.ElementAtOrDefault(1),
                    YearFrom = ParseInt(f.ElementAtOrDefault(2)),
                    YearTo = ParseInt(f.ElementAtOrDefault(3)),
                    Position = f.ElementAtOrDefault(4),
                    SourceUrl = sourceUrl
                });
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                record.Sources.Add(new SourceRef
                {
                    Url = sourceUrl,
                    Organization = get("source_organization"),
                    Type = get("source_type"),
                    Accessed = get("accessed"),
                    Supports = ["mpn", "fitment", "specs"]
                });
            }

            result.Add(new FeedRow<PartRecord>(row, record));
        }

        return result;
    }

    /// <summary>
    /// Supplier offer CSV (',' or ';' separated). Columns: brand, mpn, supplier_sku, currency, purchase_cost,
    /// stock_quantity, lead_time_days, verified_on (yyyy-MM-dd). Decimals use '.' ("12.50"); a lone ',' is accepted too.
    /// </summary>
    public static List<FeedRow<OfferRecord>> ReadOffersCsv(string content)
    {
        var firstLine = content.Split('\n', 2)[0];
        var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';

        return [.. ReadCsv(content, delimiter).Select(x => new FeedRow<OfferRecord>(x.Row, new OfferRecord
        {
            Brand = x.Get("brand"),
            Mpn = x.Get("mpn"),
            SupplierSku = x.Get("supplier_sku"),
            Currency = x.Get("currency"),
            PurchaseCost = ParseDecimal(x.Get("purchase_cost")),
            StockQuantity = ParseInt(x.Get("stock_quantity")),
            LeadTimeDays = ParseInt(x.Get("lead_time_days")),
            VerifiedOn = DateTime.TryParseExact(x.Get("verified_on"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null
        }))];
    }

    public static int? ParseInt(string value)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    public static decimal? ParseDecimal(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!value.Contains('.') && value.Count(c => c == ',') == 1)
        {
            value = value.Replace(',', '.');
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static List<string> SplitList(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0)];

    private static IEnumerable<(int Row, Func<string, string> Get)> ReadCsv(string content, char delimiter)
    {
        var lines = ParseCsv(content, delimiter);
        if (lines.Count == 0)
        {
            throw new FeedFormatException("The CSV file is empty.");
        }

        var header = lines[0]
            .Select((name, index) => (Name: name.Trim().TrimStart('﻿').ToLowerInvariant(), Index: index))
            .GroupBy(x => x.Name)
            .ToDictionary(x => x.Key, x => x.First().Index);

        for (var i = 1; i < lines.Count; i++)
        {
            var fields = lines[i];
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string Get(string column)
                => header.TryGetValue(column, out var index) && index < fields.Count
                    ? fields[index].Trim().NullEmpty()
                    : null;

            // Row numbers count the header as row 1, like a spreadsheet.
            yield return (i + 1, Get);
        }
    }

    /// <summary>
    /// RFC 4180 parser: quoted fields, doubled quotes, delimiters and line breaks inside quotes.
    /// </summary>
    internal static List<List<string>> ParseCsv(string content, char delimiter)
    {
        var lines = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                lines.Add(fields);
                fields = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (inQuotes)
        {
            throw new FeedFormatException("The CSV file ends inside a quoted field.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            lines.Add(fields);
        }

        return lines;
    }
}
