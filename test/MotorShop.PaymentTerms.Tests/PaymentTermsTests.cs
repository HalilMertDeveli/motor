using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MotorShop.PaymentTerms;
using MotorShop.PaymentTerms.Configuration;
using MotorShop.PaymentTerms.Services;
using NUnit.Framework;

namespace MotorShop.PaymentTerms.Tests;

[TestFixture]
public class PaymentTermsTests
{
    [Test]
    public void Catalog_terms_are_cash_within_10_days_then_30_60_90_days()
    {
        var terms = DealerTerms.GetTerms(new PaymentTermsSettings());

        Assert.That(terms.Select(x => x.Value), Is.EqualTo(new[] { "cash", "30", "60", "90" }));
        Assert.That(terms[0].IsCash, Is.True);
        Assert.That(terms[0].Days, Is.EqualTo(10));
        Assert.That(terms.Skip(1).All(x => !x.IsCash), Is.True);
    }

    [Test]
    public void Term_list_ignores_invalid_duplicate_and_out_of_range_values()
    {
        var terms = DealerTerms.GetTerms(new PaymentTermsSettings { TermDays = "90; 30,x, 30 0 400 60", CashTermDays = -1 });

        Assert.That(terms.Select(x => x.Days), Is.EqualTo(new[] { 30, 60, 90 }), "no cash option with an invalid cash term");
    }

    [TestCase("cash", true)]
    [TestCase(" CASH ", true)]
    [TestCase("60", true)]
    [TestCase("45", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void Only_offered_terms_are_accepted(string value, bool accepted)
    {
        Assert.That(DealerTerms.Find(new PaymentTermsSettings(), value).HasValue, Is.EqualTo(accepted));
    }

    [Test]
    public void Active_methods_follow_the_catalog_and_keep_other_choices()
    {
        var result = PaymentTermsSetup.UpdateActiveMethods(["Payments.CashOnDelivery", "Payments.PayInStore", "Payments.PayPalStandard"]);

        Assert.That(result, Does.Contain("Payments.PayPalStandard"), "other admin choices are kept");
        Assert.That(result, Does.Not.Contain("Payments.PayInStore"));
        Assert.That(result.Count(x => x == "Payments.CashOnDelivery"), Is.EqualTo(1));
        Assert.That(result, Is.SupersetOf(PaymentTermsDefaults.ActivatedMethods));
    }

    [Test]
    public void Active_methods_update_is_idempotent()
    {
        var once = PaymentTermsSetup.UpdateActiveMethods([]);
        var twice = PaymentTermsSetup.UpdateActiveMethods(new List<string>(once));

        Assert.That(twice, Is.EqualTo(once));
    }

    [Test]
    public void Bank_transfer_description_has_placeholders_and_no_invented_account()
    {
        Assert.That(PaymentTermsDefaults.PrepaymentDescription, Does.Contain("[IBAN"));
        Assert.That(PaymentTermsDefaults.PrepaymentDescription, Does.Not.Match(@"TR\d{2}"), "no IBAN may be invented");
    }

    [Test]
    public void Turkish_and_english_resources_have_the_same_keys()
    {
        static string[] Keys(string file)
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Smartstore.sln")))
            {
                root = root.Parent;
            }

            var path = Path.Combine(root!.FullName, "src", "Smartstore.Modules", "MotorShop.PaymentTerms", "Localization", file);

            return XDocument.Load(path).Root.Elements("LocaleResource").Select(x => (string)x.Attribute("Name")).Order().ToArray();
        }

        Assert.That(Keys("resources.tr-tr.xml"), Is.EqualTo(Keys("resources.en-us.xml")));
    }
}
