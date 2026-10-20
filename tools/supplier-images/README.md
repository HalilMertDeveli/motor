# Supplier picture enhancement

Improves the product pictures of a supplier catalog package (`products.json` + `images/`, the format
`MotorShop.SupplierCatalog` imports) before it is uploaded.

**By default only genuine pixels are used.** Nothing is generated or invented. The optional AI mode
(below) enlarges the genuine photos with an AI upscaler; it is an owner-approved exception, see
[docs/motor/product-images.md](../../docs/motor/product-images.md).

## What it does

1. Each package picture is scored against every image embedded in the supplier's PDF catalog on the
   product's page (`pdfPage`) and its neighbours: multi-scale normalized cross-correlation on grayscale
   thumbnails (shape) plus the mean colour of the product pixels. Scores are cached in `matches.json`.
2. Pictures and originals of a page are then paired, best similarity first, and every original is used at
   most once. A high shape score is enough; a lower one (package crops are often cut off or carry catalog
   labels such as "KRAKEN") needs a matching colour as well. An original that two pictures want almost
   equally (colour variants sharing one photo) goes to neither. Neighbouring pages need an almost perfect score.
3. With a match, the PDF original is used: native resolution, complete (page crops often cut products
   off), its soft mask composited on white.
4. Cleanup: product bounding box trimmed from the white background, light non-local-means denoising
   against JPEG blocks, Lanczos resize (at most 2.5×, gentle unsharp mask), centred on a white square
   canvas (600 to 1000 px); the product always fills 88 % of it. JPEG quality 90+, 4:4:4.
5. Without a reliable match, the package picture gets the same cleanup but is not enlarged.

`image-report.json` in the output lists the source and match score of every picture.

## Usage

Requires `ffmpeg`/`ffprobe` and poppler's `pdfimages` on PATH, and Node 20+.

```sh
pdfimages -all -p catalog.pdf work/pdfimg/img
pdfimages -list catalog.pdf > work/pdf-list.txt
node tools/supplier-images/enhance-supplier-images.mjs \
  --products=package/products.json --images=package/images \
  --pdf-images=work/pdfimg --pdf-list=work/pdf-list.txt --out=work/enhanced
```

Add `--reuse-matches=1` to re-render from `matches.json` without matching again (minutes instead of about 40 minutes for 1000 pictures).

Then zip `products.json` with `work/enhanced/images` as `images/` and import it in
Admin → Plugins → Supplier catalog import. Pictures are only added to products that have none, so
delete the old pictures of already imported products first (or import into a fresh store).

## Limits

The result can only be as good as the source. The Motosev 2026 PDF embeds its photos at about 100 ppi
(often around 300 × 300 px, strongly compressed), so the pictures are clean and complete but not
high-resolution. For studio quality, ask the supplier for the original product photos (or a print PDF)
and import those instead.

## Supplier's own photos from the web (preferred)

Better than any cleanup of PDF crops: the supplier's and manufacturers' own product photos. Motosev publishes its
studio photos (up to 3500 × 3500 px) in its web shop (motosev.com, WooCommerce), SHAD on shad.es (800 × 600 px).

```sh
# optional: SHAD reference catalog (pages of shad.es that match the package's SHAD products; slow site, ~15 min)
node tools/supplier-images/crawl-shad-references.mjs --out=work/shad-references.json --products=package/products.json --concurrency=3

node tools/supplier-images/fetch-web-photos.mjs \
  --products=enhanced/products.json --images=enhanced/images \
  --store=https://www.motosev.com --references=work/shad-references.json --out=work/web
```

- `--images` should be the genuine-pixel package (above): its pictures are used for the visual check and for every
  product without a web photo.
- Pairing: model code (catalog code without the price code) and every variant word must occur in the shop product's
  name. Then the shop photos are compared with the package picture (shape: normalized cross-correlation, colour:
  mean product colour). Accepted are only pairs where the package picture was cut from the shop photo (same photo),
  or the single exact name match that also looks alike. SHAD products are accepted on an exact reference
  ("H0NG77IF") from the reference catalog.
- Up to `--max-photos` (4) photos per product, the matched one first; trimmed and centred on a white square canvas
  of at most `--canvas` (1200) px. Photos are never enlarged.
- Output: `products.json` + `images/` (a complete package), `photo-report.json`, and `review.html`: package picture
  next to the chosen web photo, with the decision. Unclear pairs are listed as `review` and keep the package picture.
- Results are cached in `cache/` and `visual-cache.json`; reruns take minutes.
- Requests are sequential with a pause (shop) or limited to `--concurrency` (SHAD); both sites allow the paths used
  in robots.txt (Store API, sitemaps, product pages without query strings).

## AI upscaling (optional, owner-approved exception)

Only use it where the owner approved it (see [docs/motor/product-images.md](../../docs/motor/product-images.md)).
It needs the portable Real-ESRGAN build (`realesrgan-ncnn-vulkan`, BSD-3-Clause, GitHub
xinntao/Real-ESRGAN, release v0.2.5.0, `realesrgan-ncnn-vulkan-20220424-windows.zip`, 45,474,481 bytes) and a
Vulkan GPU. The binary is not part of the repository.

```sh
node tools/supplier-images/enhance-supplier-images.mjs \
  --products=package/products.json --images=package/images \
  --pdf-images=work/pdfimg --pdf-list=work/pdf-list.txt \
  --matches=work/enhanced/matches.json --reuse-matches=1 \
  --ai-upscaler=path/to/realesrgan-ncnn-vulkan.exe --out=work/enhanced-ai
```

- Same assignment as the genuine run (reuse its `matches.json`); write to a separate `--out`.
- Input per picture: the PDF original at native size, or the package picture shrunk back by
  `FALLBACK_NATIVE_DIVISOR` (2). The upscaler (4×, `realesrgan-x4plus`) also removes JPEG artefacts; the result
  then gets the same white square canvas.
- Review: `review.html` and `review.csv` list pictures in three tiers: (1) text found by Windows OCR
  (`detect-text.ps1`: brand names, model codes, catalog labels), (2) PDF original changed strongly, (3) package
  picture changed strongly (`--review-change`, default 15). `review-src/` holds the genuine native pictures for
  comparison. The list is a pre-selection: check at least these before publishing. `--review-only=1` rebuilds it
  from `image-report.json` without running the upscaler again.
- `--only` limits rendering; the assignment always considers all pictures, so variants are not mixed up.

