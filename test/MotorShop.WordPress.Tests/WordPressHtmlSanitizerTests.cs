using System;
using MotorShop.WordPress.Services;
using NUnit.Framework;

namespace MotorShop.WordPress.Tests;

[TestFixture]
public class WordPressHtmlSanitizerTests
{
    static readonly Uri Base = new("https://cms.example.com/");
    static readonly string[] EmbedHosts = ["www.youtube-nocookie.com", "player.vimeo.com"];

    private static string Clean(string html)
        => new WordPressHtmlSanitizer().Sanitize(html, Base, EmbedHosts, link => BlogPaths.RewriteWordPressLink(link, "rehberler"));

    [TestCase("<p>a</p><script>alert(1)</script>")]
    [TestCase("<img src=x onerror=alert(1)>")]
    [TestCase("<a href=\"javascript:alert(1)\">x</a>")]
    [TestCase("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [TestCase("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>")]
    [TestCase("<svg><script>alert(1)</script></svg>")]
    [TestCase("<object data=\"https://evil.example/x.swf\"></object>")]
    [TestCase("<embed src=\"https://evil.example/x\">")]
    [TestCase("<form action=\"https://evil.example\"><input name=card></form>")]
    [TestCase("<div style=\"background:url(javascript:alert(1))\">x</div>")]
    [TestCase("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>")]
    [TestCase("<p onclick=\"alert(1)\">x</p>")]
    [TestCase("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [TestCase("<base href=\"https://evil.example/\">")]
    [TestCase("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">")]
    [TestCase("<link rel=stylesheet href=\"https://evil.example/x.css\">")]
    [TestCase("<img srcset=\"javascript:alert(1) 1x\">")]
    public void Removes_script_vectors(string html)
    {
        var result = Clean(html).ToLowerInvariant();

        Assert.That(result, Does.Not.Contain("<script"));
        Assert.That(result, Does.Not.Contain("javascript:"));
        Assert.That(result, Does.Not.Contain("onerror"));
        Assert.That(result, Does.Not.Contain("onclick"));
        Assert.That(result, Does.Not.Contain("data:text/html"));
        Assert.That(result, Does.Not.Contain("<object"));
        Assert.That(result, Does.Not.Contain("<embed"));
        Assert.That(result, Does.Not.Contain("<form"));
        Assert.That(result, Does.Not.Contain("<input"));
        Assert.That(result, Does.Not.Contain("style="));
        Assert.That(result, Does.Not.Contain("<base"));
        Assert.That(result, Does.Not.Contain("<meta"));
        Assert.That(result, Does.Not.Contain("<link"));
    }

    [Test]
    public void Keeps_editor_content()
    {
        var html = """
            <h2 id="zincir">Zincir</h2>
            <p>Metin <strong>kalın</strong> <em>eğik</em> <a href="https://www.example.org/x">dış link</a></p>
            <figure class="wp-block-image size-large"><img src="/wp-content/uploads/a.jpg" alt="Zincir" width="800" height="600"><figcaption>Alt yazı</figcaption></figure>
            <ul><li>bir</li></ul><blockquote><p>alıntı</p></blockquote>
            <table><tr><td>1</td></tr></table>
            """;

        var result = Clean(html);

        Assert.That(result, Does.Contain("<h2 id=\"zincir\">Zincir</h2>"));
        Assert.That(result, Does.Contain("<strong>kalın</strong>"));
        Assert.That(result, Does.Contain("href=\"https://www.example.org/x\""));
        Assert.That(result, Does.Contain("class=\"wp-block-image size-large\""));
        Assert.That(result, Does.Contain("src=\"https://cms.example.com/wp-content/uploads/a.jpg\""), "relative media URL resolved against WordPress");
        Assert.That(result, Does.Contain("loading=\"lazy\""));
        Assert.That(result, Does.Contain("<figcaption>Alt yazı</figcaption>"));
        Assert.That(result, Does.Contain("<blockquote>"));
        Assert.That(result, Does.Contain("<td>1</td>"));
    }

    [Test]
    public void Allows_only_listed_video_embeds_and_sandboxes_them()
    {
        var result = Clean("""
            <iframe src="https://www.youtube-nocookie.com/embed/abc" allow="autoplay; camera; encrypted-media" allowfullscreen></iframe>
            <iframe src="https://evil.example/embed"></iframe>
            <iframe src="http://player.vimeo.com/video/1"></iframe>
            """);

        Assert.That(result, Does.Contain("https://www.youtube-nocookie.com/embed/abc"));
        Assert.That(result, Does.Contain("sandbox=\"allow-scripts allow-same-origin allow-presentation allow-popups allow-popups-to-escape-sandbox\""));
        Assert.That(result, Does.Contain("allow=\"autoplay; encrypted-media\""), "camera is not an allowed feature");
        Assert.That(result, Does.Not.Contain("evil.example"));
        Assert.That(result, Does.Not.Contain("http://player.vimeo.com"), "embeds need https");
    }

    [Test]
    public void Filters_classes_ids_and_headings()
    {
        var result = Clean("""<h1 class="d-none wp-block-heading">Başlık</h1><div id="content" class="fixed-top alignwide modal">x</div>""");

        Assert.That(result, Does.Contain("<h2 class=\"wp-block-heading\">Başlık</h2>"));
        Assert.That(result, Does.Not.Contain("<h1"));
        Assert.That(result, Does.Not.Contain("d-none"));
        Assert.That(result, Does.Not.Contain("fixed-top"));
        Assert.That(result, Does.Not.Contain("modal"));
        Assert.That(result, Does.Not.Contain("id=\"content\""));
        Assert.That(result, Does.Contain("class=\"alignwide\""));
    }

    [Test]
    public void Rewrites_links_to_wordpress_and_secures_new_tabs()
    {
        var result = Clean("""
            <a href="https://cms.example.com/2026/10/zincir-bakimi/#adim-2">iç</a>
            <a href="/category/rehberler/">rehber</a>
            <a href="https://cms.example.com/category/bakim/">kategori</a>
            <a href="https://cms.example.com/wp-content/uploads/kilavuz.pdf">pdf</a>
            <a href="https://www.example.org/" target="_blank">dış</a>
            <a href="https://www.example.org/" target="_top">top</a>
            """);

        Assert.That(result, Does.Contain("href=\"/blog/zincir-bakimi#adim-2\""));
        Assert.That(result, Does.Contain("href=\"/rehberler\""));
        Assert.That(result, Does.Contain("href=\"/blog/kategori/bakim\""));
        Assert.That(result, Does.Contain("href=\"https://cms.example.com/wp-content/uploads/kilavuz.pdf\""), "media files stay on WordPress");
        Assert.That(result, Does.Contain("target=\"_blank\" rel=\"noopener noreferrer\""));
        Assert.That(result, Does.Not.Contain("_top"));
    }

    [Test]
    public void Plain_text_decodes_entities_but_keeps_markup_out()
    {
        Assert.That(WordPressHtmlSanitizer.ToPlainText("Zincir &#8211; bak&#305;m"), Is.EqualTo("Zincir – bakım"));
        Assert.That(WordPressHtmlSanitizer.ToPlainText("<p>Özet [&hellip;]</p>"), Is.EqualTo("Özet …"));
        Assert.That(WordPressHtmlSanitizer.ToPlainText("<b>a</b><script>alert(1)</script>"), Does.Not.Contain("<"));
        Assert.That(WordPressHtmlSanitizer.ToPlainText(null), Is.Empty);
    }
}
