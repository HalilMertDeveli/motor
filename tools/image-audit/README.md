# Image audit

Inventory, integrity checks and rendered-page QA for the product photos of a running MRT Motors store.
Everything is read-only except `apply-fixes.mjs`. Requires Node 22.5+ and `ffmpeg` on PATH;
`render-audit.mjs` also needs `npm i` here (playwright-core) and a Chromium (Playwright's download or Chrome).

| Script | What it does |
|---|---|
| `inventory.mjs` | Every `MediaFile`, its storage file, largest thumbnail and product links: missing/invalid/truncated files, DB size mismatch, low resolution, product cut by the frame, tiny product, stale thumbnail, duplicate photo in a gallery, same photo on products of different colours, `MainPictureId` not the first photo, dangling references. Writes `inventory.csv`, `defects.csv`, `summary.md`, `contact-sheets/<defect>.html`. Exit 1 on open errors. |
| `render-audit.mjs` | Opens the storefront at 1440×900, 768×1024 and 390×844, loads lazy pictures and checks each visible `<img>`: broken, cropped by `object-fit: cover`, stretched, clipped, enlarged beyond the file. Opens the product photo viewer and checks it starts at fit-to-screen. Exit 1 on open defects. |
| `apply-fixes.mjs` | Applies the verified decisions in `fixes.json` (remove wrong/duplicate links, put a complete photo first, trim empty space around tiny drawings). Backs up the DB and every replaced file first; idempotent. **Stop the store first.** |
| `fixes.json` | The decisions, each with a reason. `reviewed` marks pictures a human judged (`OK` or `NEEDS_SOURCE`) so the audits stop failing on them. |

```sh
node tools/image-audit/inventory.mjs --tenant=src/Smartstore.Web/App_Data/Tenants/Default --out=reports/image-audit
node tools/image-audit/render-audit.mjs --base=http://127.0.0.1:5107 --out=reports/image-audit/regression
node tools/image-audit/apply-fixes.mjs --tenant=... --backup=E:/motor-backups/<stamp>     # store stopped
node --test tools/image-audit/*.test.mjs
```

Heuristics are a pre-selection, not proof: check the contact sheets before adding a decision to `fixes.json`.
Colour variants photographed in the same pose are not duplicates (`samePhoto` compares chroma), and SHAD kits
cropped to the rear of the bike (`../supplier-images/crop-bike-photos.mjs`) are expected to be cut on one side.

Corrections made in the database are not in the supplier package: the importer never replaces existing pictures,
so they survive "update existing products", but a fresh import of the package brings the original links back.
Run `apply-fixes.mjs` after such an import.

Media URLs carry `?ver=` (migration `MediaUrlVersion`), so a file corrected in place is fetched again instead of the
copy browsers cache for 7 days.
