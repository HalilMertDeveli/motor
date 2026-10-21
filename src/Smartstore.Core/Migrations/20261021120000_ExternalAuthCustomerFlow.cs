using FluentMigrator;
using Smartstore.Core.Localization;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: messages of the external sign-in flow (Google), explicit account linking and the
/// "or register with email" divider. Resources only, no schema or data changes.
/// </summary>
[MigrationVersion("2026-10-21 12:00:00", "MOTOR: External authentication customer flow")]
internal class ExternalAuthCustomerFlow : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        void Add(string key, string en, string de, string tr)
            => builder.AddOrUpdate(key).Value(en).Value("de", de).Value("tr", tr);

        Add("Account.Register.OrRegisterWithEmail",
            "or register with your email address",
            "oder mit E-Mail-Adresse registrieren",
            "veya e-posta adresinizle kayıt olun");

        Add("Account.AssociatedExternalAuth.LinkHint",
            "Link an external account to sign in with it in the future. Your orders, addresses and cart stay with this customer account.",
            "Verknüpfen Sie ein externes Konto, um sich künftig damit anzumelden. Bestellungen, Adressen und Warenkorb bleiben in diesem Kundenkonto.",
            "Bir dış hesabı bağlayarak bundan sonra onunla giriş yapabilirsiniz. Siparişleriniz, adresleriniz ve sepetiniz bu müşteri hesabında kalır.");

        Add("Account.AssociatedExternalAuth.Link",
            "Link {0} account",
            "{0}-Konto verknüpfen",
            "{0} hesabını bağla");

        Add("ExternalAuthentication.Linked",
            "Your {0} account is now linked. You can sign in with it from now on.",
            "Ihr {0}-Konto ist jetzt verknüpft. Sie können sich ab sofort damit anmelden.",
            "{0} hesabınız bağlandı. Bundan sonra bu hesapla giriş yapabilirsiniz.");

        Add("ExternalAuthentication.AlreadyLinked",
            "This {0} account is already linked to your customer account.",
            "Dieses {0}-Konto ist bereits mit Ihrem Kundenkonto verknüpft.",
            "Bu {0} hesabı zaten müşteri hesabınıza bağlı.");

        Add("ExternalAuthentication.LinkOnInfoPage",
            "You are already signed in. To sign in with {0} in the future, link the account here.",
            "Sie sind bereits angemeldet. Um sich künftig mit {0} anzumelden, verknüpfen Sie das Konto hier.",
            "Zaten giriş yaptınız. Bundan sonra {0} ile giriş yapmak için hesabı buradan bağlayın.");

        Add("ExternalAuthentication.Error.AccessDenied",
            "Sign-in was cancelled. You can try again or sign in with your email address and password.",
            "Die Anmeldung wurde abgebrochen. Sie können es erneut versuchen oder sich mit E-Mail-Adresse und Passwort anmelden.",
            "Giriş işlemi iptal edildi. Tekrar deneyebilir veya e-posta adresiniz ve şifrenizle giriş yapabilirsiniz.");

        Add("ExternalAuthentication.Error.SessionExpired",
            "The sign-in session has expired or is invalid. Please try again.",
            "Die Anmeldesitzung ist abgelaufen oder ungültig. Bitte versuchen Sie es erneut.",
            "Giriş oturumunun süresi doldu veya oturum geçersiz. Lütfen tekrar deneyin.");

        Add("ExternalAuthentication.Error.EmailNotVerified",
            "Your Google account does not have a verified email address. Please verify it at Google or register with your email address.",
            "Ihr Google-Konto hat keine bestätigte E-Mail-Adresse. Bitte bestätigen Sie sie bei Google oder registrieren Sie sich mit Ihrer E-Mail-Adresse.",
            "Google hesabınızın e-posta adresi doğrulanmamış. Lütfen adresinizi Google'da doğrulayın veya e-posta adresinizle kayıt olun.");

        Add("ExternalAuthentication.Error.EmailMissing",
            "{0} did not provide an email address. Please register with your email address.",
            "{0} hat keine E-Mail-Adresse übermittelt. Bitte registrieren Sie sich mit Ihrer E-Mail-Adresse.",
            "{0} bir e-posta adresi iletmedi. Lütfen e-posta adresinizle kayıt olun.");

        Add("ExternalAuthentication.Error.EmailInUse",
            "A customer account with this email address already exists. Please sign in with your email address and password, then link your {0} account under \"My account > Customer info\".",
            "Es gibt bereits ein Kundenkonto mit dieser E-Mail-Adresse. Bitte melden Sie sich mit E-Mail-Adresse und Passwort an und verknüpfen Sie Ihr {0}-Konto unter \"Mein Konto > Kundeninfo\".",
            "Bu e-posta adresiyle kayıtlı bir müşteri hesabı zaten var. Lütfen e-posta adresiniz ve şifrenizle giriş yapın, ardından {0} hesabınızı \"Hesabım > Müşteri bilgileri\" sayfasından bağlayın.");

        Add("ExternalAuthentication.Error.AccountNotActive",
            "Your customer account is not active (yet). Please confirm your email address or contact us.",
            "Ihr Kundenkonto ist (noch) nicht aktiv. Bitte bestätigen Sie Ihre E-Mail-Adresse oder kontaktieren Sie uns.",
            "Müşteri hesabınız (henüz) etkin değil. Lütfen e-posta adresinizi doğrulayın veya bizimle iletişime geçin.");

        Add("ExternalAuthentication.Error.LinkFailed",
            "The account could not be linked. Please try again.",
            "Das Konto konnte nicht verknüpft werden. Bitte versuchen Sie es erneut.",
            "Hesap bağlanamadı. Lütfen tekrar deneyin.");

        Add("ExternalAuthentication.Error.LinkedToOtherAccount",
            "This {0} account is already linked to another customer account.",
            "Dieses {0}-Konto ist bereits mit einem anderen Kundenkonto verknüpft.",
            "Bu {0} hesabı başka bir müşteri hesabına bağlı.");
    }
}
