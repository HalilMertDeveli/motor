# Supplier picture enhancement

Improves the product pictures of a supplier catalog package (`products.json` + `images/`, the format
`MotorShop.SupplierCatalog` imports) before it is uploaded.

**Only genuine pixels are used.** Nothing is generated or invented (no AI upscaling, no generated photos).

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
