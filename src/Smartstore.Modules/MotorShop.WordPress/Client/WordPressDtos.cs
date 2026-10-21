using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorShop.WordPress.Client;

// Shapes of the WordPress REST API v2 responses (only the fields MOTOR uses).
// See https://developer.wordpress.org/rest-api/reference/

public class WpRendered
{
    [JsonPropertyName("rendered")]
    public string? Rendered { get; set; }

    [JsonPropertyName("protected")]
    public bool Protected { get; set; }
}

public class WpPost
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("date_gmt")]
    public DateTime? DateGmt { get; set; }

    [JsonPropertyName("modified_gmt")]
    public DateTime? ModifiedGmt { get; set; }

    [JsonPropertyName("title")]
    public WpRendered? Title { get; set; }

    [JsonPropertyName("content")]
    public WpRendered? Content { get; set; }

    [JsonPropertyName("excerpt")]
    public WpRendered? Excerpt { get; set; }

    [JsonPropertyName("featured_media")]
    public int FeaturedMedia { get; set; }

    [JsonPropertyName("categories")]
    public int[]? Categories { get; set; }

    [JsonPropertyName("tags")]
    public int[]? Tags { get; set; }

    /// <summary>
    /// Only present when Yoast SEO is installed.
    /// </summary>
    [JsonPropertyName("yoast_head_json")]
    public WpYoastHead? YoastHead { get; set; }

    [JsonPropertyName("_embedded")]
    public WpEmbedded? Embedded { get; set; }
}

public class WpEmbedded
{
    [JsonPropertyName("author")]
    public WpAuthor[]? Author { get; set; }

    [JsonPropertyName("wp:featuredmedia")]
    public WpMedia[]? FeaturedMedia { get; set; }

    /// <summary>
    /// One array per taxonomy (categories, tags).
    /// </summary>
    [JsonPropertyName("wp:term")]
    public WpTerm[][]? Terms { get; set; }
}

public class WpAuthor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class WpTerm
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("taxonomy")]
    public string? Taxonomy { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("parent")]
    public int Parent { get; set; }
}

public class WpMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("alt_text")]
    public string? AltText { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("media_details")]
    public WpMediaDetails? MediaDetails { get; set; }
}

public class WpMediaDetails
{
    // WordPress sends "" instead of a number for some file types.
    [JsonPropertyName("width")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int Height { get; set; }

    [JsonPropertyName("sizes")]
    public Dictionary<string, WpMediaSize>? Sizes { get; set; }
}

public class WpMediaSize
{
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("width")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int Height { get; set; }
}

public class WpYoastHead
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("og_title")]
    public string? OgTitle { get; set; }

    [JsonPropertyName("og_description")]
    public string? OgDescription { get; set; }
}

/// <summary>
/// One page of a WordPress collection with the totals from the X-WP-Total and X-WP-TotalPages headers.
/// </summary>
public class WpCollection<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>
/// Reads a number, a numeric string or anything else (as 0).
/// </summary>
public sealed class LenientIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var n) ? n : 0;
            case JsonTokenType.String:
                return int.TryParse(reader.GetString(), out var s) ? s : 0;
            default:
                reader.Skip();
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
