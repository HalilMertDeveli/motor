# Supplier picture enhancement

Improves the product pictures of a supplier catalog package (`products.json` + `images/`, the format
`MotorShop.SupplierCatalog` imports) before it is uploaded.

**Only genuine pixels are used.** Nothing is generated or invented (no AI upscaling, no generated photos).

## What it does

1. Each package picture is matched to the original image embedded in the supplier's PDF catalog, on the
   product's page (`pdfPage`). Matching uses multi-scale normalized cross-correlation on grayscale
   thumbnails plus a mean-colour check, so colour variants (black / blue) are not mixed up. A neighbouring
   page is only accepted with an almost perfect score.
2. With a match, the PDF original is used: native resolution, complete (page crops often cut products
   off), its soft mask composited on white.
3. Cleanup: product bounding box trimmed from the white background, light non-local-means denoising
   against JPEG blocks, Lanczos resize (at most 2.5×, gentle unsharp mask), centred on a white square
   canvas (600 to 1000 px) so every product fills the frame the same way. JPEG quality 90+, 4:4:4.
4. Without a reliable match, the package picture gets the same cleanup but is not enlarged.

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

Then zip `products.json` with `work/enhanced/images` as `images/` and import it in
Admin → Plugins → Supplier catalog import. Pictures are only added to products that have none, so
delete the old pictures of already imported products first (or import into a fresh store).

## Limits

The result can only be as good as the source. The Motosev 2026 PDF embeds its photos at about 100 ppi
(often around 300 × 300 px, strongly compressed), so the pictures are clean and complete but not
high-resolution. For studio quality, ask the supplier for the original product photos (or a print PDF)
and import those instead.
