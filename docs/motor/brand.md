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
| Admin texts that named the platform ("MRT Motors hakkında", "MRT Motors sürümü", update and package messages, module descriptions), storefront `<meta name="generator">` | tr language packs, migration `MrtMotorsAdminBrand`, `Views/Shared/Layouts/_Document.cshtml` |
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

## Product brand: Prosev is sold as MRT Motors

The supplier Motosev's house brand **Prosev** is sold under the store brand (owner decision
2026-10-22: "hiç bir prosev olmasın sitemde", "Markayı MRT Motors yap"; the owner was told that
this changes the manufacturer shown to customers and that product photos may still show Prosev
logos).

- Migration `20261022140000_MrtMotorsProductBrand` renames the manufacturer "Prosev" to
  "MRT Motors", replaces the whole word "Prosev" in product names, short and full descriptions
  (and their localized values), and moves the active URL slugs: `/prosev/` → `/mrt-motors/`,
  `/prosev-…/` → `/mrt-motors-…/`. The old slugs stay as inactive URL records, so old links
  redirect. SKUs, MPNs (`CE2700-…`) and pictures are not changed. Idempotent.
- `MotorShop.SupplierCatalog` applies the same replacement to every import
  (`SupplierCatalogImporter.ApplyStoreBrand`), so a re-import of the Motosev package does not
  bring "Prosev" back.
- Not changed: `tools/supplier-images` works on the supplier package and the supplier's website,
  where the brand is still Prosev (it matches by SKU and brand tokens before the import), and
  `MotorShop.CatalogImport` (generic parts feeds; its brand keys identify products). Product
  photos are not edited.

## Platform name (Smartstore) in the admin

Owner request 2026-10-27: "bütün marka isimleri mrt motors olacak". Migration `20261027120000_MrtMotorsAdminBrand`
replaces "Smartstore" in the Turkish admin texts of existing databases (only rows that still contain the old text and
that no admin edited); the tr app pack and the module packs carry the same texts for new installs. The test
`Turkish_Texts_Name_The_Store_Not_The_Platform` keeps the packs clean.

Still says Smartstore, on purpose:

- the copyright notice of the software on Admin > About ("Copyright © SmartStore AG", source links) and the
  "by SmartStore AG" line in the storefront footer: AGPL-3.0 legal notices of the platform the store runs on;
- the author column of upstream modules and themes (their copyright holder);
- technical names: module system names (`Smartstore.PayPal`), package file names (`Smartstore.Module.*.zip`),
  configuration keys, web addresses (translate.smartstore.com), assembly names on the system information page.

## What stays Prosev

- Code identifiers (`ProsevStoreDefaults`, `prosev-*` CSS classes, `_prosev.scss`, migration
  class names) and the Fly app `prosev` / Supabase `prosev-prod` names are not visible on the site.
- The public demo address moved from `prosev.tail41fdd8.ts.net` to `mrtmotors.tail41fdd8.ts.net`
  (Tailscale machine name, see the live demo notes); old `prosev.…` links stop working.

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

- **Google sign-in after the address change**: in Google Cloud Console > Clients, add the authorized
  redirect URI `https://mrtmotors.tail41fdd8.ts.net/signin-google` (the old `prosev.…` URI no
  longer works), or Google login fails with redirect_uri_mismatch.

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
