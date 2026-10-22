using System.Linq;
using NUnit.Framework;
using Smartstore.Core.Catalog.Search;

namespace Smartstore.Core.Tests.Catalog.Search;

[TestFixture]
public class CatalogTermIndexTests
{
    private CatalogTermIndex _index;

    [OneTimeSetUp]
    public void Setup()
    {
        _index = CatalogTermIndex.Build(
        [
            Doc(1, "Scoyco 869 Shark Kapalı Kask", "S869-0220", "Scoyco", ["Kask", "Kapalı Kask"], "Maksimum güvenlik: Full face tasarımıyla, DOT ve CE sertifikalı."),
            Doc(2, "Scoyco FF-871 Yaksha Kapalı Kask", "FF-871-01780-YAKSHA", "Scoyco", ["Kask", "Kapalı Kask"], "Full face · ABS materyal"),
            Doc(3, "MTS 966 Drago Çene Açılır Kask", "M966-02450", "MTS", ["Kask", "Çene Açılır Kask"], "Çene açılır tasarım"),
            Doc(4, "Free-M FR-612 Bella Açık Kask", "FR-612-01650-BELLA", "Free-M", ["Kask", "Açık Kask"], "Jet kask"),
            Doc(5, "MTS 966 Kask Çene Pedi", "M966-0130", "MTS", ["Kask Aksesuarları"], "Yedek parça"),
            Doc(6, "Scoyco MC109 Kırmızı Motosiklet Eldiveni", "MC109-01290-KIRMIZI", "Scoyco", ["Eldiven"], "Dokunmatik parmak"),
            Doc(7, "Prosev 027 Rature Black 4 Mevsim Mont", "027-03990-BLACK", "Prosev", ["Giyim", "Mont"], "Su geçirmez"),
            Doc(8, "Scoyco K360 Body Armor Koruma Ceketi", "K360-03490", "Scoyco", ["Koruma Ekipmanları"], "CE koruma"),
            Doc(9, "Shad SH58X Genişletilebilir Arka Çanta", "D0B58100-08950", "SHAD", ["Çanta", "Arka Çanta (Top Case)"], "58 litre"),
            Doc(10, "Prosev Q7 Motosiklet İnterkom", "Q7-02990", "Prosev", ["İnterkom"], "Kaskınıza kolayca takılır"),
            Doc(11, "MTS XT91 Kask Telefon ve Aksiyon Kamera Tutucu", "XT91-0450", "MTS", ["Telefon Tutucu"], "Full face kask uyumlu"),
            Doc(12, "Shad Y011CS Yamaha MT07 Depo Üstü Bağlantı Demiri", "Y011CS-01250", "SHAD", ["Bağlantı Demirleri ve Kitler"], null),
            Doc(13, "MTS WS21 CNC Alüminyum Rüzgarlık Yamaha MT-07", "WS21-CNC-0395", "MTS", ["Ön Siperlik"], null)
        ]);
    }

    private static CatalogTermDocument Doc(int id, string name, string sku, string brand, string[] categories, string description)
        => new()
        {
            Id = id,
            Name = name,
            ShortDescription = description,
            Codes = [sku],
            Brands = [brand],
            Categories = categories
        };

    private int[] Rank(string term) => _index.Rank(term).ProductIds;

    [TestCase("KAPALI KASK")]
    [TestCase("kapali kask")]
    [TestCase("Kapalı Kask")]
    [TestCase("kapalı KASK")]
    public void Turkish_letters_and_case_are_ignored(string term)
    {
        var ids = Rank(term);
        Assert.That(ids.Take(2), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ids, Does.Not.Contain(6));
    }

    [TestCase("ELDİVEN")]
    [TestCase("eldiven")]
    [TestCase("ELDIVEN")]
    [TestCase("eldıven")]
    public void Dotted_and_dotless_i_match(string term)
    {
        Assert.That(Rank(term), Is.EqualTo(new[] { 6 }));
    }

    [TestCase("canta", 9)]
    [TestCase("çanta", 9)]
    [TestCase("interkom", 10)]
    [TestCase("intercom", 10)]
    [TestCase("İNTERKOM", 10)]
    public void Finds_single_product_by_word(string term, int expectedId)
    {
        Assert.That(Rank(term).First(), Is.EqualTo(expectedId));
    }

    [Test]
    public void Exact_sku_ranks_first()
    {
        Assert.That(Rank("M966-0130").First(), Is.EqualTo(5));
        Assert.That(Rank("m9660130").First(), Is.EqualTo(5));
    }

    [Test]
    public void Model_number_without_punctuation_matches()
    {
        Assert.That(Rank("ff871"), Is.EqualTo(new[] { 2 }));
        Assert.That(Rank("FF-871").First(), Is.EqualTo(2));
    }

    [TestCase("MT-07")]
    [TestCase("mt07")]
    [TestCase("yamaha mt 07")]
    public void Model_codes_match_with_and_without_separators(string term)
    {
        Assert.That(Rank(term), Is.EquivalentTo(new[] { 12, 13 }));
    }

    [Test]
    public void Turkish_suffixes_match_the_catalog_word()
    {
        // "montu" → "Mont", generic "motosiklet" is optional.
        Assert.That(Rank("motosiklet montu").First(), Is.EqualTo(7));
        Assert.That(Rank("eldivenleri"), Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public void Synonyms_find_catalog_words()
    {
        var ids = Rank("motor ceketi");
        Assert.That(ids, Does.Contain(7));
        Assert.That(ids, Does.Contain(8));
        // A direct word match ranks above the synonym match.
        Assert.That(ids.First(), Is.EqualTo(8));
    }

    [Test]
    public void Full_face_finds_closed_helmets()
    {
        var ids = Rank("full face kask");
        Assert.That(ids.Take(2), Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void Helmets_rank_above_helmet_accessories()
    {
        var ids = Rank("kask");
        Assert.That(ids.Take(4), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        Assert.That(ids, Does.Contain(5));
        // Only the description of the intercom mentions "kask".
        Assert.That(ids, Does.Not.Contain(10));
    }

    [Test]
    public void Brand_search_finds_all_brand_products()
    {
        Assert.That(Rank("scoyco"), Is.EquivalentTo(new[] { 1, 2, 6, 8 }));
        Assert.That(Rank("SHAD"), Is.EquivalentTo(new[] { 9, 12 }));
    }

    [Test]
    public void One_letter_typo_is_tolerated()
    {
        Assert.That(Rank("eldvien"), Is.EqualTo(new[] { 6 }));
        Assert.That(Rank("scoyc0"), Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("-- !!")]
    [TestCase("xyzqq")]
    [TestCase("<script>alert(1)</script>")]
    public void Unknown_or_empty_terms_find_nothing(string term)
    {
        Assert.That(Rank(term), Is.Empty);
    }

    [Test]
    public void Buckets_follow_ranking_order()
    {
        var ranking = _index.Rank("kask");
        Assert.That(ranking.Buckets.SelectMany(x => x), Is.EqualTo(ranking.ProductIds));
    }

    [TestCase("İstanbul KIRMIZI ışık", "istanbul kirmizi isik")]
    [TestCase("ŞĞÜÖÇ şğüöç", "sguoc sguoc")]
    public void Fold_maps_turkish_letters(string input, string expected)
    {
        Assert.That(SearchTextNormalizer.Fold(input), Is.EqualTo(expected));
    }

    [Test]
    public void HighlightHtml_encodes_and_marks_folded_matches()
    {
        var html = SearchTextNormalizer.HighlightHtml("Kapalı <Kask>", "kapali kask", "<b>", "</b>");
        Assert.That(html, Is.EqualTo("<b>Kapalı</b> &lt;<b>Kask</b>&gt;"));
    }

    [Test]
    public void MatchesAllWords_uses_word_prefixes()
    {
        Assert.That(SearchTextNormalizer.MatchesAllWords("Çene Açılır Kask", "cene acil"), Is.True);
        Assert.That(SearchTextNormalizer.MatchesAllWords("Çene Açılır Kask", "kapali"), Is.False);
    }
}
