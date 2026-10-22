# Store brand: MRT Motors

On 2026-10-22 the owner renamed the store from **Prosev** to **MRT Motors** and asked for its
contact data everywhere the store shows contact information (source: Instagram profile
[@mrtmotorservis](https://www.instagram.com/mrtmotorservis/), "Motosiklet Bayisi"):

- Address: Mustafa Kemal Paşa Mahallesi, Yıldırım Beyazıt Caddesi No: 10/B, Parseller, Avcılar / İstanbul
- Phone: 0537 490 68 32 (the landline on the logo is cut off and left out until the owner gives it)
- Instagram: https://www.instagram.com/mrtmotorservis/

The values live in `ProsevStoreDefaults` (seed) and, at runtime, in the admin settings
(company information, contact data, social), so the owner can change them without a release.

## What says MRT Motors

| Place | Source |
| --- | --- |
| Storefront header wordmark (hexagon "M" badge + MRT MOTORS) | `Themes/MotorDark/Views/Shared/Components/ShopLogo/Default.cshtml`, `_prosev.scss` |
| Admin header wordmark, admin favicon, admin browser title | `Areas/Admin/.../AdminNavbar/Default.cshtml`, `wwwroot/admin/images/mrt-motors-icon.svg`, resource `Admin.PageTitle` |
| Theme name in Admin > Themes ("MRT Motors Dark") | `Themes/MotorDark/theme.config` |
| Home hero, story and footer texts | resources `Themes.MotorDark.*` (migration `ProsevStore`) |
| Store name, e-mail sender name, SEO meta title (so page titles, e-mails, legal pages) | `ProsevStoreDefaults.StoreName`, `TrTRSeedData` |
| Payment terms module name in the admin | `MotorShop.PaymentTerms` module.json and resources |
| Footer contact block (address with map link, phone, Instagram) | `Themes/MotorDark/Views/Shared/Layouts/_Layout.cshtml`, reads the settings below |
| Footer social icon (Instagram only) | `SocialSettings.InstagramLink`; the other "#" placeholders are cleared |
| Seller name, address and phone in the legal documents (ön bilgilendirme, mesafeli satış) | `{{ Seller.* }}` from `CompanyInformationSettings` / `ContactDataSettings` |
| Organization JSON-LD (address, telephone, sameAs Instagram), e-mail and PDF footers | same settings |
| Contact, about us and imprint pages | topics `ContactUs`, `AboutUs`, `Imprint` |

E-mail templates and the legal documents (mesafeli satış, KVKK) use the store name and the
company data from the database (`{{ Store.Name }}`, company settings), so they follow the
store name.

## What stays Prosev

- **Prosev** is also a product brand of the supplier Motosev (e.g. "Prosev Q7 Motosiklet
  İnterkom", manufacturer "Prosev"). Products, the manufacturer, their URL slugs and photos
  are supplier data and keep that name.
- Code identifiers (`ProsevStoreDefaults`, `prosev-*` CSS classes, `_prosev.scss`, migration
  class names) and infrastructure names (Tailscale host `prosev.tail41fdd8.ts.net`, Fly app
  `prosev`, Supabase `prosev-prod`) are not visible as the store name and are unchanged.

## Existing databases

Migration `20261022120000_MrtMotorsRebrand` (data seeder, runs on start-up) is additive and
idempotent. It only replaces values that are still the old Prosev default:

- `Store.Name`, `EmailAccount.DisplayName`, settings `SeoSettings.MetaTitle` and
  `CompanyInformationSettings.CompanyName` equal to "Prosev" become "MRT Motors";
- the seeded placeholder company "Prosev [firma unvanı]" becomes "MRT Motors [firma unvanı]";
- the brand-bearing resources (storefront texts, admin title, payment terms module name) are
  updated unless an admin edited them;
- company address (street, street 2, city, state, country), company phone and the Instagram
  link are filled only while empty (or "#"); the other social "#" placeholders are cleared;
- the store admin's seeded placeholder address ("Örnek Mah. ...", "+90 212 000 00 00") gets
  the real address and phone;
- the ContactUs, AboutUs and Imprint topics get the contact text only while they still have the
  install text ("... buraya ekleyin").

A name or text that an admin already changed is kept. Back up the database before the first
start with the new version (see the redeploy steps of the live demo).

## Manual steps for the owner

- **Google sign-in**: Google Cloud Console > Google Auth Platform > Branding, set the app
  name to "MRT Motors" (the consent screen shows it). See [google-login.md](google-login.md).
- Legal company title, tax number, MERSİS, IBAN and a contact e-mail remain placeholders until
  the owner enters them in the admin (Configuration > Settings > General > Company information /
  Contact data). The postal code is not known and left empty.
- The landline (0212 812 77 …) can be added as the hotline number once the owner gives it.
- The original logo file (red MRT, white MOTORS, rider mark) can replace the wordmark: upload it
  as the store logo and switch on `motor-use-logo`.
- An uploaded store logo is only shown when the theme variable `motor-use-logo` is on
  (Admin > Themes > MRT Motors Dark); otherwise the MRT MOTORS wordmark is used.
