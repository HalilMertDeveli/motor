using System;
using System.Linq;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Services;
using NUnit.Framework;

namespace MotorShop.WordPress.Tests;

[TestFixture]
public class BlogPostMapperTests
{
    static readonly Uri Base = new("https://cms.example.com/");
    private readonly BlogPostMapper _mapper = new(new WordPressHtmlSanitizer());
    private readonly WordPressOptions _options = TestOptions.Create();

    [TestCase("draft")]
    [TestCase("private")]
    [TestCase("future")]
    [TestCase("pending")]
    [TestCase(null)]
    public void Rejects_unpublished(string? status)
    {
        Assert.That(_mapper.Map(WpSamples.Post(1, "x", status!), _options, Base, true), Is.Null);
    }

    [Test]
    public void Rejects_password_protected_and_incomplete_items()
    {
        var post = WpSamples.Post(1, "gizli");
        post.Content!.Protected = true;

        Assert.That(_mapper.Map(post, _options, Base, true), Is.Null);
        Assert.That(_mapper.Map(WpSamples.Post(0, "x"), _options, Base, true), Is.Null);
        Assert.That(_mapper.Map(WpSamples.Post(1, ""), _options, Base, true), Is.Null);
        Assert.That(_mapper.Map(null, _options, Base, true), Is.Null);
    }

    [Test]
    public void Maps_post_with_sections_terms_and_product_skus()
    {
        var wp = WpSamples.Post(5, "bak%c4%b1m-rehberi", title: "Bak&#305;m &lt;rehberi&gt;", content: "<p>bir iki üç</p><script>x()</script>",
            terms: [WpSamples.Category(3, "rehberler", "Rehberler"), WpSamples.Tag(8, "sku-pr-1001"), WpSamples.Tag(9, "zincir", "Zincir")]);

        var post = _mapper.Map(wp, _options, Base, includeContent: true)!;

        Assert.That(post.Slug, Is.EqualTo("bakım-rehberi"), "percent encoded slug decoded");
        Assert.That(post.Section, Is.EqualTo(BlogSection.Guides));
        Assert.That(post.Title, Is.EqualTo("Bakım <rehberi>"), "plain text, encoded by Razor on output");
        Assert.That(post.Excerpt, Is.EqualTo("Kısa özet …"));
        Assert.That(post.ContentHtml, Does.Not.Contain("script"));
        Assert.That(post.ProductSkus, Is.EqualTo(new[] { "pr-1001" }));
        Assert.That(post.Tags.Select(x => x.Slug), Is.EqualTo(new[] { "zincir" }), "SKU tags are not shown as tags");
        Assert.That(post.PublishedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(post.ReadingMinutes, Is.EqualTo(1));
    }

    [Test]
    public void List_items_do_not_keep_content()
    {
        var post = _mapper.Map(WpSamples.Post(5, "a", content: "<p>uzun içerik</p>"), _options, Base, includeContent: false)!;

        Assert.That(post.ContentHtml, Is.Empty);
        Assert.That(post.Section, Is.EqualTo(BlogSection.Blog));
    }

    [Test]
    public void Pages_get_the_page_section()
    {
        var wp = WpSamples.Post(5, "garanti");
        wp.Type = "page";

        Assert.That(_mapper.Map(wp, _options, Base, true)!.Section, Is.EqualTo(BlogSection.Page));
    }

    [Test]
    public void Maps_image_with_srcset_and_card_size()
    {
        var image = BlogPostMapper.MapImage(new WpMedia
        {
            SourceUrl = "https://cms.example.com/a.jpg",
            AltText = "Zincir &amp; dişli",
            MediaType = "image",
            MediaDetails = new WpMediaDetails
            {
                Width = 1600,
                Height = 900,
                Sizes = new()
                {
                    ["medium"] = new WpMediaSize { SourceUrl = "https://cms.example.com/a-300.jpg", Width = 300, Height = 169 },
                    ["medium_large"] = new WpMediaSize { SourceUrl = "https://cms.example.com/a-768.jpg", Width = 768, Height = 432 },
                    ["evil"] = new WpMediaSize { SourceUrl = "javascript:alert(1)", Width = 10, Height = 10 }
                }
            }
        })!;

        Assert.That(image.CardUrl, Is.EqualTo("https://cms.example.com/a-768.jpg"));
        Assert.That(image.SrcSet, Is.EqualTo("https://cms.example.com/a-300.jpg 300w, https://cms.example.com/a-768.jpg 768w"));
        Assert.That(image.Alt, Is.EqualTo("Zincir & dişli"));

        Assert.That(BlogPostMapper.MapImage(new WpMedia { SourceUrl = "javascript:alert(1)" }), Is.Null);
        Assert.That(BlogPostMapper.MapImage(new WpMedia { SourceUrl = "https://cms.example.com/a.mp4", MediaType = "file" }), Is.Null);
    }

    [TestCase("zincir-bakimi", true)]
    [TestCase("bakım-rehberi", true)]
    [TestCase("ab_12", true)]
    [TestCase("../etc/passwd", false)]
    [TestCase("a/b", false)]
    [TestCase("<script>", false)]
    [TestCase("Zincir", false)]
    [TestCase("a b", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void Validates_slugs(string? slug, bool valid)
    {
        Assert.That(BlogPostMapper.IsValidSlug(slug), Is.EqualTo(valid));
    }
}
