# Storefront search and product photo viewer

## Search

Smartstore's built-in (LINQ) catalog search only does a substring match on name, SKU and short description
and has no relevance order. MOTOR adds an in-memory term index for storefront searches
(search page and instant search), independent of the database provider (SQLite, PostgreSQL, SQL Server).

Code: `src/Smartstore.Core/Catalog/Search/Relevance/`

| File | Purpose |
| --- | --- |
| `SearchTextNormalizer.cs` | Turkish folding (`ı/İ/I → i`, `ş → s`, `ğ → g`, `ü → u`, `ö → o`, `ç → c`), tokenizing, safe highlighting |
| `CatalogTermIndex.cs` | Matching and ranking |
| `CatalogSearchSynonyms.cs` | The small synonym and generic-word lists |
| `CatalogTermIndexProvider.cs` | Builds and caches the index; a save hook invalidates it, and it is rebuilt at least every 5 minutes |

How a term matches: every significant word must match a word of the product (exact, word prefix, a Turkish
suffix such as `montu → Mont`, a synonym, or a one-letter typo for words of 4+ letters). Generic words like
"motosiklet" or "motor" are optional. Codes match without punctuation (`ff871 → FF-871`, `MT-07 → MT07`).

Ranking, highest first: exact SKU / part number / GTIN, exact product name, the whole phrase in the name,
all words in the name, name words, brand, category, tags, short description. Products that only mention
the words in their description are dropped when better matches exist. Explicit sort orders (price, name,
newest) still apply; the ranking is used for the default "relevance" order.

The ranked IDs are passed to the normal Smartstore query, so publication, store, ACL, visibility, stock and
facet filters work as before. If building the index fails, search falls back to the old database match and
logs the error. Catalogs above 20,000 products also use the old database match.

### Changing synonyms

Edit `CatalogSearchSynonyms.cs`. Keys and values are folded (lowercase, no Turkish letters). Only map a word
customers type to the word the catalog actually uses (`ceket → mont`). Add a test to
`test/Smartstore.Core.Tests/Catalog/Search/CatalogTermIndexTests.cs`.

### Instant search

`Themes/MotorDark/Views/Search/Partials/InstantSearch*.cshtml`: product photo, brand, price (price
visibility permissions still apply), matching categories and brands, and "Tüm sonuçları görüntüle".
Requests are debounced (250 ms) and outdated requests are aborted (`wwwroot/js/public.search.js`).
The search box (`Themes/MotorDark/Views/Shared/Components/SearchBox/Default.cshtml`) has a visible submit button.

## Product photo viewer

The core gallery (thumbnails, slick slider, hover zoom, PhotoSwipe 4) stays in place.
`Themes/MotorDark/wwwroot/js/motor-gallery.js` adds:

- on the main photo: previous/next buttons, a counter and an "İncele" button;
- in the viewer: dark UI with Turkish labels, zoom in/out buttons, mouse-wheel zoom at the cursor,
  `+` / `-` / `0` keys, a thumbnail strip, focus back to the opener on close.
  Pinch, double tap, swipe and drag-to-pan come from PhotoSwipe.

Zoom stops at 2x the photo's real pixel size. The viewer loads the original upload (the largest real file);
nothing is upscaled or sharpened. `smartstore.gallery.js` fires `init.pswp` on `#pswp` after opening the viewer.

When a variant with its own photos is selected, Smartstore re-renders `Product.Media`; the script re-attaches.

## Response compression (recommended)

The app serves `theme.css` (about 590 KB) and the scripts uncompressed. Turning on response compression in
the admin performance settings (`PerformanceSettings.UseResponseCompression`, off by default) or compressing
at the proxy cuts `theme.css` to about 100 KB (gzip).
