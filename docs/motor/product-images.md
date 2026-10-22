# Product images

## Rule

MOTOR uses genuine, authorized product photographs. Generating product photos with AI is not allowed: no
pictures of products that were not photographed, no invented variants, colours or accessories.

## Official product photos (preferred source)

**Decision:** the store owner, 2026-10-20: use the original, professional photos from the official product pages
instead of the pictures cut out of the PDF catalog, wherever a page clearly shows the same product.

**Rights:** product photos are copyrighted by the brands. Using them needs the brand's permission or Motosev's
distributor rights; the owner is responsible for that agreement. `sources.csv` of every run records where each
picture came from, so a picture can be removed on request.

**Sources:** only official sites, see `SITES` in `tools/supplier-images/official-images.mjs`:

- `www.motosev.com`: the supplier's own shop (Motosev owns the Prosev brand and distributes the other brands of
  the catalog: Scoyco, MTS, Free-M, Fazer, Nikko, Pro Helmets, Kinguard, GAFF and more);
- `www.shad.es`: SHAD, for SHAD products, matched by the SHAD reference.

Never marketplaces, resellers or AI-generated pictures. Requests follow robots.txt, one per second; logins and bot
protection are never bypassed.

**No guessing:** a picture is replaced only when the model code, the variant (colour, graphic) and the product type
agree and the official picture agrees with the catalog picture (shape and colour check), or when the name is not
conclusive but the official page shows the very same photo as the catalog picture. Products without a catalog
picture only get pictures from an exact manufacturer reference (SHAD). Everything else keeps its catalog picture and
is listed in `review.html` as "Belirsiz" (uncertain) or "Bulunamadı" (not found). Products are imported unpublished.

**Uncertain photos are never used** (decision: the store owner, 2026-10-22, "belirsiz ürün fotoğraflarını tamamen
kaldır"). There is no manual review of the uncertain list: an uncertain product keeps its catalog picture, even when
another tool or a person found an official photo for it. Before importing a package that was assembled from more
than one run, run `official-images.mjs check` against the official run's `review.csv`; it fails when an uncertain
product carries anything but its catalog pictures. Applied on 2026-10-22: the merged Motosev 2026 package
(`motosev-2026-paket-birlesik.zip`, official run plus 72 photos from closed PR #39) carried such photos for 45 of the
109 uncertain products and was replaced by `motosev-2026-paket-birlesik-belirsizsiz.zip`.

**Way back:** the previous packages stay on the Desktop; re-import one of them into a store whose picture links of
the affected products were removed (the importer never replaces existing pictures).

## SHAD mounting kits: no whole motorcycles

**Why:** SHAD photographs its mounting kits fitted to a whole motorcycle; customers took the store for a motorcycle
dealer. On 2026-10-22 the live demo's 351 such photos were cropped to the mounting area.

**Rule:** the last step of the package build, `tools/supplier-images/crop-bike-photos.mjs`, crops every whole-bike
photo of a SHAD kit product to the rear of the bike (see the README for the tests). Cropping only removes pixels of the
genuine photo. Known false positives and bikes facing left are listed in `bike-photo-exceptions.json`.

## Exception: AI upscaling of the Motosev 2026 catalog photos

**Decision:** the store owner, 2026-10-20, after comparing samples (genuine pixels next to AI upscaling).

**Why:** the only available source, the Motosev 2026 PDF catalog (`2026 KATALOG 12.10.2026.pdf`), embeds its
photos at about 100 ppi (mostly 100 to 330 px, heavily compressed). Even after the genuine-pixel cleanup
(`tools/supplier-images`, PR #35) the pictures stay soft. Motosev's original photos are not available yet.

**Scope of the exception:**

- Only enlarging existing, authorized Motosev catalog photos (Real-ESRGAN `realesrgan-x4plus`, 4×).
- The product, its shape, colour and variant come from the genuine photo. Nothing is added, removed or
  composited; no new photos are generated.
- Only for the Motosev 2026 package. Any other supplier or catalog needs its own owner decision.

**Known risks:** the upscaler invents fine detail. Small text, logos and model codes can be re-drawn wrongly
(in a test, a helmet's small brand logo became unreadable letters), and fabric texture can look smoother than
in reality. Large logos and shapes were reproduced well in the samples.

**Review before publishing:** `review.html` / `review.csv` from the AI run list the pictures that need a human
check:

- `text`: Windows OCR found text in the picture (brand names, model codes);
- `strong-change`: the upscaler changed local structure strongly (95th percentile of the difference between its
  output, scaled back to native size, and the genuine picture is at least 15 grey levels).

The list is an automatic pre-selection, not a guarantee: small changes can also occur in pictures that are not
listed. For any wrong picture, use the file with the same name from the genuine-pixel package. Products are
imported unpublished, so nothing reaches customers before the admin publishes it.

**Way back:** the genuine-pixel package (`motosev-2026-paket-iyilestirilmis.zip`) stays available. As soon as
Motosev provides original photos (or a print-quality PDF), replace the AI pictures with them.

## Tools

- Genuine-pixel cleanup and AI mode: [`tools/supplier-images/README.md`](../../tools/supplier-images/README.md).
- Import: Admin → Plugins → Supplier catalog import (`MotorShop.SupplierCatalog`). Pictures are only added to
  products that have none: delete the old pictures of already imported products first, or import into a fresh store.
- Storefront picture sizes: `MotorMediaDefaults` (cards 512 px, product page 1024 px, quality 90).
