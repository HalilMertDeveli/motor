using System.Net;
using AngleSharp.Dom;
using Ganss.Xss;
using Smartstore.Utilities.Html;

namespace MotorShop.WordPress.Services;

/// <summary>
/// Cleans HTML from the WordPress editor before it is rendered on the storefront.
/// </summary>
/// <remarks>
/// Allow list, not block list: scripts, event handlers, forms, inline styles, objects and unknown iframes are removed.
/// Video iframes are kept only for <see cref="WordPressOptions.AllowedEmbedHosts"/> and are sandboxed.
/// Links to the WordPress site are rewritten to the MOTOR blog URLs, so that visitors and crawlers stay on MOTOR.
/// </remarks>
public class WordPressHtmlSanitizer
{
    static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "blockquote", "br", "caption", "cite", "code", "col", "colgroup", "dd", "del", "details", "div",
        "dl", "dt", "em", "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "iframe", "img", "ins",
        "kbd", "li", "mark", "ol", "p", "picture", "pre", "q", "s", "small", "source", "span", "strong", "sub", "summary",
        "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr", "u", "ul", "video", "audio", "track"
    };

    static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "target", "rel", "src", "srcset", "sizes", "alt", "width", "height", "class", "id", "colspan", "rowspan",
        "scope", "datetime", "cite", "start", "reversed", "type", "media", "controls", "poster", "preload", "loop", "muted",
        "playsinline", "kind", "srclang", "label", "allow", "allowfullscreen", "open"
    };

    // WordPress block and alignment classes. Everything else could pick up theme or Bootstrap styles (e.g. "d-none", "fixed-top").
    static readonly string[] AllowedClassPrefixes = ["wp-", "has-", "is-", "align", "size-", "attachment-", "blocks-", "columns-", "alignment-"];

    static readonly string[] AllowedIframeFeatures = ["accelerometer", "autoplay", "clipboard-write", "encrypted-media", "gyroscope", "picture-in-picture", "fullscreen", "web-share"];

    private readonly Ganss.Xss.HtmlSanitizerOptions _sanitizerOptions;

    public WordPressHtmlSanitizer()
    {
        _sanitizerOptions = new Ganss.Xss.HtmlSanitizerOptions
        {
            AllowedTags = AllowedTags,
            AllowedAttributes = AllowedAttributes,
            AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" },
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src", "cite", "poster" },
            AllowedCssProperties = new HashSet<string>(),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>()
        };
    }

    /// <summary>
    /// Sanitizes post or page content.
    /// </summary>
    /// <param name="html">Rendered content from the REST API.</param>
    /// <param name="wordPressBase">WordPress root. Relative URLs are resolved against it and links to it are rewritten.</param>
    /// <param name="allowedEmbedHosts">Hosts allowed as iframe source.</param>
    /// <param name="rewriteLink">Maps a link to the WordPress site to a MOTOR path, or <c>null</c> to keep it.</param>
    public string Sanitize(string? html, Uri? wordPressBase, IReadOnlyCollection<string> allowedEmbedHosts, Func<Uri, string?>? rewriteLink = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sanitizer = new HtmlSanitizer(_sanitizerOptions) { KeepChildNodes = false };
        sanitizer.PostProcessDom += (_, e) => PostProcess(e.Document, (IParentNode?)e.Document.Body ?? e.Document, wordPressBase, allowedEmbedHosts, rewriteLink);

        return sanitizer.Sanitize(html, wordPressBase?.ToString() ?? string.Empty).Trim();
    }

    /// <summary>
    /// Plain text of a rendered WordPress field (title, excerpt): tags removed and entities decoded.
    /// The result must still be HTML encoded on output (Razor does that).
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(HtmlUtility.StripTags(html));
        // The automatic excerpt ends with " [&hellip;]".
        text = text.Replace("[…]", "…").Replace("[...]", "…");

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void PostProcess(IDocument document, IParentNode context, Uri? wordPressBase, IReadOnlyCollection<string> embedHosts, Func<Uri, string?>? rewriteLink)
    {
        // The post title is the only h1 on the page.
        foreach (var h1 in context.QuerySelectorAll("h1").ToList())
        {
            var h2 = document.CreateElement("h2");
            foreach (var attr in h1.Attributes.ToList())
            {
                h2.SetAttribute(attr.Name, attr.Value);
            }
            h2.Append([.. h1.ChildNodes]);
            h1.Replace(h2);
        }

        foreach (var element in context.QuerySelectorAll("*").ToList())
        {
            FilterClasses(element);

            // Ids only on headings (anchor links); elsewhere they could collide with ids of the page.
            if (element.HasAttribute("id") && element.LocalName is not ("h2" or "h3" or "h4" or "h5" or "h6"))
            {
                element.RemoveAttribute("id");
            }

            switch (element.LocalName)
            {
                case "iframe":
                    ProcessIframe(element, embedHosts);
                    break;
                case "a":
                    ProcessLink(element, wordPressBase, rewriteLink);
                    break;
                case "img":
                case "source":
                    ProcessImage(element);
                    break;
            }
        }
    }

    private static void FilterClasses(IElement element)
    {
        if (!element.HasAttribute("class"))
        {
            return;
        }

        var keep = element.ClassList.Where(c => AllowedClassPrefixes.Any(p => c.StartsWith(p, StringComparison.Ordinal))).ToList();
        if (keep.Count == 0)
        {
            element.RemoveAttribute("class");
        }
        else
        {
            element.SetAttribute("class", string.Join(' ', keep));
        }
    }

    private static void ProcessIframe(IElement iframe, IReadOnlyCollection<string> embedHosts)
    {
        var src = iframe.GetAttribute("src");
        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !embedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)))
        {
            iframe.Remove();
            return;
        }

        var allow = (iframe.GetAttribute("allow") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => AllowedIframeFeatures.Contains(f.Split(' ')[0], StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (allow.Count > 0)
        {
            iframe.SetAttribute("allow", string.Join("; ", allow));
        }
        else
        {
            iframe.RemoveAttribute("allow");
        }

        // Players need scripts and their own origin; they still cannot reach the MOTOR page or navigate it.
        iframe.SetAttribute("sandbox", "allow-scripts allow-same-origin allow-presentation allow-popups allow-popups-to-escape-sandbox");
        iframe.SetAttribute("loading", "lazy");
        iframe.SetAttribute("referrerpolicy", "strict-origin-when-cross-origin");
    }

    private static void ProcessLink(IElement link, Uri? wordPressBase, Func<Uri, string?>? rewriteLink)
    {
        var href = link.GetAttribute("href");

        if (href.HasValue() && wordPressBase != null && rewriteLink != null
            && Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && uri.Host.Equals(wordPressBase.Host, StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = rewriteLink(uri);
            if (rewritten != null)
            {
                link.SetAttribute("href", rewritten);
            }
        }

        if (link.GetAttribute("target").HasValue())
        {
            if (!link.GetAttribute("target")!.Equals("_blank", StringComparison.OrdinalIgnoreCase))
            {
                link.RemoveAttribute("target");
            }
            else
            {
                link.SetAttribute("rel", "noopener noreferrer");
            }
        }
    }

    private static void ProcessImage(IElement image)
    {
        // srcset is not a URI attribute for the sanitizer, so its candidates are checked here.
        var srcset = image.GetAttribute("srcset");
        if (srcset.HasValue())
        {
            var candidates = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var safe = candidates.All(c =>
            {
                var url = c.Split(' ', 2)[0];
                return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
            });

            if (!safe)
            {
                image.RemoveAttribute("srcset");
                image.RemoveAttribute("sizes");
            }
        }

        if (image.LocalName == "img")
        {
            image.SetAttribute("loading", "lazy");
            image.SetAttribute("decoding", "async");
            if (!image.HasAttribute("alt"))
            {
                image.SetAttribute("alt", string.Empty);
            }
        }
    }
}
