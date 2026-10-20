# Product images

## Rule

MOTOR uses genuine, authorized product photographs. Generating product photos with AI is not allowed: no
pictures of products that were not photographed, no invented variants, colours or accessories.

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
