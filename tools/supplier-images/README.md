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


## Official product photos (`official-images.mjs`)

Replaces the catalog pictures with the original photos from the official product pages, where a page clearly
shows the same product. Owner decision and rules: [docs/motor/product-images.md](../../docs/motor/product-images.md).

Sites (`SITES` in the script): `www.motosev.com` (the supplier's own shop, owner of the Prosev brand; it lists the
products of all brands it distributes) and `www.shad.es` (SHAD, only for SHAD products, matched by the SHAD
reference). Both are read through their public WooCommerce Store API; robots.txt allows it. No marketplaces,
no resellers, no generated pictures.

```sh
node tools/supplier-images/official-images.mjs index    --out=work/web
node tools/supplier-images/official-images.mjs match    --products=package/products.json --web=work/web --out=work/matches.json
node tools/supplier-images/official-images.mjs download --matches=work/matches.json --out=work/download
node tools/supplier-images/official-images.mjs resolve  --products=package/products.json --images=package/images \
  --matches=work/matches.json --download=work/download --out=work/matches-resolved.json
node tools/supplier-images/official-images.mjs download --matches=work/matches-resolved.json --out=work/download
node tools/supplier-images/official-images.mjs build    --products=package/products.json --images=package/images \
  --matches=work/matches-resolved.json --download=work/download --out=work/official \
  --fallback-images=improved/images [--reject=SKU1,SKU2]
node tools/supplier-images/official-images.mjs check    --products=package/products.json --review=work/official/review.csv
```

- `match`: model code (catalog code without its price group, e.g. `869-01780` -> `869`), brand, variant words
  (Turkish and Spanish colour names mapped to the English ones) and product type (a helmet page never stands in
  for a visor or spare part). A page is used only when exactly one page fits; when the same article is on both
  sites, the manufacturer's page wins. A letter prefix on either side (`910` / `M-910`, `FR-965` / `965`) counts as
  a weaker match. Several pages, a missing or different variant word, or a letter-only code (`MKM`, `DO SPACY`)
  make the product "uncertain".
- `download`: pictures of matched products (at most 5, the main picture of every colour page first, the WordPress
  size of at least 1200 px) plus 500 px pictures of uncertain candidates. One request per second, stops on HTTP
  429/503, resumable (`download-log.json`).
- `resolve`: an uncertain product is matched only when exactly one candidate page shows the very same photo as the
  catalog picture (crop-aware shape score at least 0.88, colour distance at most 15, clearly ahead of the other
  candidates). Run `download` again afterwards for the full pictures.
- `build`: compares the catalog picture with the official pictures: crop-aware grey NCC on 96 px thumbnails (the
  catalog crops are cut and carry labels) plus the mean product colour, which always has to agree (a near-identical
  shape in another colour is another colour variant). Products that fail keep their catalog picture and are listed
  as "uncertain"; so are SKUs given in `--reject`. The site's first picture stays the main picture unless it fails
  the check (some pages show another colour first). Official pictures get the same white square canvas as the
  catalog pictures (trimmed, product fills 88 %, 800 to 1200 px, at most 1.5× enlargement, JPEG q2 4:4:4);
  photos with a scene keep their frame.
- Output: `products.json` (same products, only `images` changed), `images/`, `sources.csv`/`sources.json` (source
  page, picture URL, owner, download time per picture) and `review.html`/`review.csv` (matched, uncertain,
  not found, per brand, with catalog and official pictures side by side).
- Products that keep their catalog picture take it from `--fallback-images` when given (same file names), else
  from the package.
- Uncertain products never get an official photo, not even after a manual review (owner decision 2026-10-22).
  `check` fails (exit code 1) when a package gives an uncertain product of `review.csv` anything but its catalog
  pictures; run it on every package that combines several runs or sources before importing it.
- Check the result on contact sheets before importing. On 2026-10-20 the supplier's site had the photos of the
  JY01 red and black helmet pages swapped; the colour rule catches such cases, but only a person can confirm them.

## Whole-bike photos of SHAD mounting kits (`crop-bike-photos.mjs`)

Many official SHAD photos of mounting kits (racks, 3P/4P side case holders, backrests) show the whole motorcycle
with the kit fitted, so customers thought the store sells motorcycles. This last step crops them to the area where
the kit is mounted. Genuine pixels only: the photo is cut, nothing is added.

```sh
node tools/supplier-images/crop-bike-photos.mjs --products=work/official/products.json --images=work/official/images \
  --out=work/cropped
node --test tools/supplier-images/crop-bike-photos.test.mjs
```

- Kit products: brand SHAD, name contains `Bağlantı`, `3P System`, `4P System` or a motorcycle make; never tank
  bags (`Depo Üstü`).
- Whole-bike photo, all four needed: (1) the four corners are white (every channel ≥ 225); (2) the bounding box of
  the non-white pixels keeps 1 % off every image edge; (3) box width / image width > 0.55 and box aspect > 1.2;
  (4) two wheels: a row between 50 and 90 % of the box height has dark runs (< 110) whose outer span is > 0.7 of the
  box width with a gap > 0.2 of it.
- Crop: the rear 55 % of the box (the left side; SHAD photographs bikes facing right) at full box height, centred on
  a white square (×1.06 margin), 1200 × 1200, JPEG q2 4:4:4.
- `bike-photo-exceptions.json`, by package file name: `keep` for false positives (close-ups and part photos that
  pass the tests), `mirror` for bikes facing left (the right side is kept). Add to it after checking `review.html`.
- Output: same `products.json` and file names, cropped pictures replaced; `bike-crops.csv` (metrics and verdict of
  every kit picture) and `review.html` (before / after).
- On the Motosev 2026 package (2026-10-22): 484 kit pictures, 351 cropped, 7 exceptions, 1 mirrored, the same set the
  live demo was cropped with by hand. The importer never replaces existing pictures: a store that already has the
  uncropped pictures needs its picture links of these products removed before a re-import.
