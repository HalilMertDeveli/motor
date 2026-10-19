# Spare-parts research notes (Wave 2), 2026-10-19

Output: `parts.json`, 78 candidate records. Built by `../build-parts.cjs` from manual lookups.
Every fitment row comes from the part manufacturer's own catalogue or lookup for that model and those years.
No price, stock, supplier, image or marketing text was collected.

## Counts

| Category | Brand | Records |
|---|---|---|
| oil-filter | Hiflofiltro | 6 (HF204, HF205, HF303, HF155, HF138, HF132) |
| air-filter | Hiflofiltro | 10 (HFA4707, HFA1623, HFA1715, HFA1935, HFA2406, HFA2610, HFA2922, HFA6303, HFA6302, HFA3611) |
| brake-pad | EBC Brakes | 20 (FA252HH, FA174HH, FA142HH, FA347HH, FA140HH, FA197HH, FA229HH, FA231HH, FA679HH, FA496HH, FA196HH, FA606HH, FA213HH, FA181HH, SFA694HH, SFA603HH, SFA693HH, SFA228HH, SFA412HH, FA388HH) |
| other (drum brake shoe) | EBC Brakes | 1 (H352) |
| brake-disc | EBC Brakes | 7 (MD2127X, MD2119X, MD2120, MD8010, MD850, MD1194X, MD9156D) |
| clutch | EBC Brakes | 4 (CK2309, CSK037, CK4527, CK5666) |
| spark-plug | NGK (Niterra) | 20 |
| sprocket | JT Sprockets | 10 (JTF1591, JTR1876, JTF565, JTR478, JTF1539, JTR486, JTF1908, JTR890ZBK, JTF520, JTR1792) |
| chain | none | 0 (see "Gaps") |
| battery | none | 0 (Yuasa blocked, see below) |

Status: 60 VERIFIED, 18 PARTIAL, 0 UNVERIFIED.
`licenseStatus` is `PUBLIC_FACTUAL_REFERENCE_ONLY` for all records. No source grants a reuse licence for its catalogue data.

## Sources

### Hiflofiltro: https://www.hiflofiltro.com/catalogue
- **Data available:** filter pages list the part number, finish variants (C = chrome, RC = racing), the "OEM numbers replaced" cross-reference list and the full application list. Model pages list every filter for one bike: oil, air and performance upgrade.
- **Access method:** static HTML lookup. WebFetch works.
  - Filter page: `https://www.hiflofiltro.com/catalogue/filter/{MPN}`.
  - Model page: `https://www.hiflofiltro.com/catalogue/model/{id}`.
  - Search forms exist (manufacturer/type/cc, free text, part number), but their result URLs were not needed.
- **Specs:** no dimensions or thread specs on the web pages. Diagrams exist as images; they were not copied.
- **API, feed or downloads:** no API and no CSV/XML found. The downloads page offers:
  - `Hiflofiltro_Application_Guide_2024.pdf` (2.8 MB). It was not downloaded.
  - Product-photo ZIP packs (133 MB full pack, plus monthly updates).
  - Logos.
- **Dealer program:** none found on the site.
- **Terms of use:** no terms, legal or copyright page is linked in the footer (checked /downloads and /catalogue). The photo ZIPs are offered with no stated licence. Treat image reuse as **permission required**.
- **Blocked:** nothing.
- **Caveat:** text was extracted with a page-to-text summariser. The long OEM cross-reference lists should be spot-checked against the live page before import.

### EBC Brakes: https://www.ebcbrakes.com/part-finder/
- **Data available:** for each make, model and year (by submodel or engine code):
  - pads (organic FA/SFA, sintered HH, EPFA, GPFAX, SFAC);
  - discs (MD standard and contour, VR floating);
  - clutch (CK kits, CSK springs, SRC/SRK) under the separate type "Motorcycle Clutch";
  - left/right/front/rear position.
  EBC says the list "may include parts not yet in production".
- **Access method:** the lookup UI is JavaScript-driven: Country (UK/EU or USA), Type, Make and Model, then one "View Parts" click per year.
  - I used the built-in browser and read the results table from the page. That was one model at a time, about 25 models.
  - Result URL pattern: `https://www.ebcbrakes.com/motorbike/uk-row/{MAKE}/{Model}/` and `/motorbike-clutch/...`. These URLs only work after making the selection in the UI. A direct load returns 404.
- **PDF catalogues** (https://www.ebcbrakes.com/catalogues/): UK Motorcycle Catalogue 205 (2026) and US Motorcycle Catalogue 304 (2024). Both are over 10 MB and could not be read by the fetch tool.
- **API or feed:** no public API. The home page shows a TecDoc (TecAlliance) logo, so EBC application data is distributed through TecDoc, which is a paid, licensed data feed. There is a "Where to buy" dealer locator and the EBC Brakes Direct shop, but no dealer data program was found.
- **Terms of use:** no website terms page is linked. The footer reads "(c) 2026 EBC Brakes. All rights reserved." Image rights: **permission required**.
- **Blocked:** nothing. The PDF catalogues were too large to fetch.

### NGK / Niterra EMEA: https://www.ngkntk.com/part-finder/motorcycles-scooter/spark-plugs/{make}/
- **Data available:** for each model row:
  - cylinders, cc, years, info (kW, construction codes, quantity notes);
  - spark gap;
  - "Standard" plug and often an "Advanced" iridium alternative.
  Detail pages add the NGK stock number, hex size, thread diameter and thread length.
- **Access method:** the model dropdown is JavaScript (I used the built-in browser to click a model). Detail pages are static and fetchable:
  `https://www.ngkntk.com/part-finder/motorcycles-scooter/spark-plugs/{make}/{slug}/`.
  The slug contains the plug code.
- **Disclaimer:** the Part Finder is valid for vehicles registered in Europe only, under normal use and professional fitting. Niterra accepts no liability.
- **API or feed:** none public. Mobile apps exist (NGK/NTK Part Finder). No 2-wheel catalogue PDF link was found on the site, although press releases mention one.
- **Terms of use:** https://www.ngkntk.com/website-terms-conditions/
  - Reproducing pages is allowed only in full and unchanged. Quotes are allowed with the source named.
  - Framing is prohibited. Links are allowed, but Niterra asks to be told.
  - There is no explicit clause on scraping or data.
  - Images come under a separate **MediaBox licence**:
    - free basic right, EMEA only;
    - only to label genuine Niterra products;
    - no modification and no sublicensing;
    - screenshots and adverts need prior approval;
    - revocable.
  - General T&C PDF: `/fileadmin/general_content_all_countries/legal/niterra_generaltermsandconditions_en-de_2026-08.pdf`.
- **Blocked:** nothing.
- **Coverage gaps in NGK EU data:**
  - MT-25 is not listed.
  - KTM 390 Duke appears only for 2013-2014 and 125 Duke only for 2011-2014.
  - Pulsar NS200 has no years.
  - Only the 2005-2007 Forza 250 (NSS250) is listed.

### JT Sprockets: https://www.jtsprockets.com/catalogue/
- **Data available:** model pages list:
  - front and rear sprockets (steel, RB rubber-cushioned, ZBK hardened, JTA aluminium);
  - stock tooth counts and available sizes;
  - chain pitch and length;
  - a gear-ratio chart.
  Part search lists every application with its tooth count.
- **Access method:** static GET forms.
  - Text search: `https://www.jtsprockets.com/catalogue/?searchtype=text&search=MT-07`.
  - Part search: `https://www.jtsprockets.com/catalogue/?searchtype=buyers&part=JTF1591`.
  - Model page: `https://www.jtsprockets.com/catalogue/model/s{id}`.
  All work with WebFetch.
- **Downloads** (https://www.jtsprockets.com/downloads/):
  - application guides as PDF: Sprockets 2024, Braking 2026, Drive Belts 2020;
  - Drive Chain Range 2025 and spec sheet;
  - photo ZIP packs (325 MB, plus monthly updates).
  No CSV/XML.
- **Dealer signup** (https://www.jtsprockets.com/dealer-signup/) is a newsletter only. There is no feed or B2B data.
- **Terms of use:** no terms or legal page is linked. Photos are offered with no stated licence, so image reuse is **permission required**.
- **Part numbers:** JT shows the base number plus the tooth count. Its search example "JTF513.15" suggests that BASE.TEETH is the orderable code.
- **Chain data:** JT model pages give a chain size (e.g. 525 x 108) but explicitly no chain part number ("refer to the vehicle manufacturer's manual").
- **Blocked:** nothing.

### Yuasa: https://www.yuasa.co.uk/ redirects to https://www.yuasa.com/uk/
- **Blocked:**
  - WebFetch returned HTTP 403.
  - The browser hit a Cloudflare "Just a moment..." challenge.
  - I did not try to bypass it. No battery records were created.
  - Retailer listings (e.g. YTZ10S for MT-07) were seen in search results but are SECONDARY only and were not used.

### Not reached in this pass
D.I.D, RK, DENSO, Brembo, K&N, and the OEM catalogues (Yamaha EU parts catalogue, Honda, Kawasaki, Suzuki, KTM) were not reached because of time. Good next sources:
- the D.I.D application chart;
- the Yamaha EU genuine-parts catalogue, for OEM numbers;
- the Hiflo and JT application-guide PDFs, as a cross-check.

## PARTIAL records and why

| MPN | Reason |
|---|---|
| HF132 | Covers only the 1999-2006 Burgman 400. The current Burgman 400 filter was not found. |
| HFA1715 | NC750X 2014-2020 only. The 2021+ air filter was not identified. |
| FA229HH, FA231HH | EBC lists both for the V-Strom 650 / Versys 650 front. My extraction merged the left/right rows, so the caliper side is not recorded. |
| FA181HH | EBC data covers only Pulsar NS200 2014-2016 and Dominar 2025. |
| SFA228HH | Two different front pads (SFA228HH and SFA197HH) are listed for Burgman 400 2007-2024. Side not recorded. |
| MD2119X | EBC's Tracer 7 front disc alternates between MD2127X and MD2119X by year. |
| CR9E | NGK row gives "1 cylinder" for the twin-cylinder YZF-R25. |
| IFR6G-11K | NGK prints a 14 mm thread / 16 mm hex for the NC750X 2021+. This looks atypical and should be verified against the Honda manual. |
| MR8K-9, CR7E, CR8EIA-9, DPR7EA-9 | Taken from the NGK model table and URL slug. The detail page (stock number, thread) was not opened. |
| LKAR8A-9 | NGK lists the KTM 390/125 Duke only for 2013-2014 / 2011-2014. Gap not stated. |
| JTF1539 | JT model page header ("2019 to 2020") disagrees with the list entry ("18-20"). |
| JTR890ZBK | JT rows show 43T for the 390 Duke 2024-26, but "available sizes" lists only 42/44/45. |
| JTF520, JTR1792 | Fitment comes from one model page only. The part page was not opened. |

## General caveats
- **Year ranges.** These are exactly what each lookup shows; I did not interpolate.
  - Gaps are split into separate fitment rows: EBC has no 2022 row for the 390 Adventure, no 2023 row for the V-Strom 650 ABS, and no 2024 row for the base Africa Twin.
  - EBC already shows a 2027 model year for the CBR650R.
  - The EBC 390 Duke front pad (FA606HH) is listed through 2025 even though the 2024 Duke is a new generation. Confirm before listing.
- **EBC position.** Front pads are usually listed for both the left and the right caliper, which means 2 sets per dual-disc bike. `packageQuantity` is null because the per-pack count was not stated on the pages read.
- **GTIN.** No source exposed a GTIN/EAN. `gtin` is null everywhere.
- **Turkish market.** No Turkish shop pages were used. All fitment comes from EU-market lookups (EBC UK/EU, NGK EMEA, Hiflo and JT UK catalogues), so Asian-market variants (YZF-R25, MT-25, Pulsar, Dominar, Apache) are thinly covered. TVS Apache RTR 200 is not covered by any source checked: EBC has no TVS make, and NGK EU has no TVS make.
