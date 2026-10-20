# Motorcycle reference dataset: research notes

Accessed 2026-10-19. The dataset is in `vehicles.json`: 92 records, one record per generation.
Five parallel research passes produced it, one per brand group. Their detailed notes are kept in `parts/<group>-notes.md`.
A validator (`../scripts/merge.cjs`) checks that every non-null factual field is listed in some source's `supports`. It also checks that no HIGH record relies on a SECONDARY source for cc or years. The final run reported 0 problems.

## Counts

| Make | Records | HIGH | MEDIUM | LOW |
|---|---|---|---|---|
| Honda | 23 | 19 | 2 | 2 |
| Yamaha | 20 | 12 | 8 | 0 |
| KTM | 11 | 6 | 3 | 2 |
| Kawasaki | 8 | 6 | 2 | 0 |
| Suzuki | 8 | 5 | 3 | 0 |
| Bajaj | 5 | 2 | 3 | 0 |
| TVS | 4 | 2 | 1 | 1 |
| BMW | 3 | 2 | 1 | 0 |
| Kymco | 3 | 0 | 3 | 0 |
| CFMOTO | 2 | 0 | 2 | 0 |
| Triumph | 2 | 2 | 0 | 0 |
| Benelli, Mondial, RKS | 1 each | 0 | 3 | 0 |
| **Total** | **92** | **56** | **31** | **5** |

- **Markets:** TR 37, EU 34, global 12, IN 9.
- **Origin:** 62 records come from the 35 seed items (split by generation). 30 are new models.
- **Size:** the total is above the ~40-60 target because the spec requires separate records for each generation. Those splits alone produce 62 records from 35 seeds.

## Seed audit (35 seed items)

- **VERIFIED (7):**
  - Kawasaki Ninja 250R, Ninja 400, Z650
  - Suzuki GSX-R125, Burgman 400
  - Honda Forza 250 (cc only)
  - Bajaj Dominar 400
  - Several of these are MEDIUM because official sources disagree on cc (see below).
- **UNVERIFIABLE (1):** Honda Dio 110. Honda India gives no cc. The 109.51 cc figure comes from Honda's Sri Lanka distributor. Nothing official supports the 2019 start.
- **CORRECTED (27):** every other seed. The main reasons:
  - **The seed merged several generations.** Examples:
    - MT-07: 2014 / 2018 / 2021 / 2025
    - NC750X: 2014 / 2016 / 2021 / 2025
    - PCX125: 2018 / 2021 / 2025
    - CB125F: 2015 / 2021 / 2026
    - Z900: 2017 / 2025
    - Versys 650: 2015 / 2022
    - V-Strom 650: 2012 / 2017
    - Africa Twin: 2020 / 2024
    - CBR650R: 2019 / 2024
    - RC 390: before 2022 / 2022+
  - **The cc was wrong:**

    | Model | Seed | Official |
    |---|---|---|
    | Honda CBR250R | 250 | 249.6 |
    | Honda CB125F 2015-2020 | 124 | 124.7 |
    | Suzuki Address 110 | 113 | 112 (113 is the 1998 two-stroke) |
    | Bajaj NS200 / RS200 | 199 | 199.5 |
    | TVS Apache RTR 200 4V | 197 | 197.75 |
    | TVS NTORQ 125 | 124 | 124.8 |
    | Yamaha YZF-R25 | 249 | 250 in every EU/TR factsheet |

  - **The engine changed inside the seed's range:**
    - KTM 390 Duke: 373 to 398.7 cc (2024+)
    - KTM 390 Adventure: 373.2 to 398.7 cc (2025+)
    - KTM 125 Duke: 124.7 to 124.99 cc
    - KTM 250 Duke: 248.8 to 249.07 cc
    - TVS Jupiter 110: new 113.3 cc model from Aug 2024
  - **The year range was contradicted:** Honda Europe archives the CBR250R as 2011-2014, not 2011-2013. No official evidence was found for a 2014-2017 Yamaha XMAX 250 in Europe.
  - **India-only models:** Honda Activa 125 and Dio 110 are not sold by Honda Türkiye.

## Policy decisions made in the final review

- **cc conflicts:** the official figure is recorded exactly as the source gives it. Where two official spec values disagree, both are listed in the record's notes and the record is capped at MEDIUM. Affected records:
  - Yamaha YZF-R25 (250 EU/TR vs 249 Yamaha global), MT-25, XMAX 250
  - Kawasaki Ninja 250R (248 vs 249)
  - Suzuki GSX-R125 (124.4 vs 124), Burgman 400 (400 vs 399)
  - Bajaj Dominar 400 (373 TR vs 373.3 Bajaj)
  - Honda Forza 250
  - Kymco Downtown GT 350 (321 vs 320)
  - Mondial Fuzion 125 (124 vs 125)
- **Marketing roundings are not counted as conflicts.** A rounded figure in marketing text ("690cc", "399 cc", "250 cc") does not count against an exact spec-table value from the same maker (689, 398.7, 249.07). The spec-table value is recorded and HIGH is kept.
- **yearTo:** set only where an official source states the end explicitly. That applies to one record: Honda CBR250R, archived by Honda Europe as "2011 - 2014".
  - In 13 records (9 Honda, 4 Kawasaki/Suzuki) the research agents had inferred yearTo from the next generation's official launch. These were changed to `yearTo: null`.
  - They now carry a new field, `successorStartYear`: the officially stated first model year of the next generation. The importer can derive an end year from it if wanted.
  - `successorStartYear` is the only field added to the requested schema. It is null on all other records.
- **Turkish model years for Honda:** honda.com.tr price lists carry "2025 Model Yılı" and "2026 Model Yılı" headings. Those years were assigned to the EU generation current in that model year, which each record's notes state. This is an inference about which generation a year belongs to, not about whether the year exists.

## Sources used, by type

- **Official manufacturer sites:**
  - Yamaha Motor Europe factsheet PDFs (cdn2.yamaha-motor.eu, EN and tr-TR) and the Yamaha global newsroom
  - hondanews.eu press kits; honda2wheelersindia.com
  - global.kawasaki.com newsroom, kawasaki.eu, storage.kawasaki.eu brochures
  - globalsuzuki.com (news, Digital Library, product pages)
  - ktm.com country model-year pages (en-int, en-ph, en-do, en-hu, it-ch), ktmindia.com
  - bajajauto.com (press releases, /en-tr); tvsmotor.com (press releases, spec pages)
  - press.bmwgroup.com; triumphmotorcycles.co.uk and Triumph Belgium; benelli.com
  - mondialmotor.com.tr and rksmotor.com.tr (Turkish brand owners)
- **Official distributors:**
  - honda.com.tr
  - kawasaki.com.tr (Z-Moto A.S.)
  - suzuki.com.tr; bikes.suzuki.co.uk
  - ekuralkan.com (Kuralkan, Bajaj TR)
  - cfmoto.com.tr (UMA)
  - kymco.com.tr (Doğan Trend Otomotiv)
  - honda.lk (Dio cc only)
- **Secondary sources:** Wikipedia, magazines, Turkish press, CarTrade and similar. These are used only as context or corroboration, and only in MEDIUM/LOW records.

## Blocked or failed sites (nothing was bypassed; no CAPTCHA or login was met)

- **Manufacturer pages that render only in a browser:** yamaha-motor.eu/tr/tr returned only the title, so the factsheet PDFs were used instead. cfmoto.com and kymco.com (global) showed no products. The honda2wheelersindia.com press list never finished loading.
- **HTTP 403:** kuralkan.com.tr, triumph-mediakits.com, vespa.com/tr_TR, cfmoto.com.au, bseindia.com filings, and many guessed Yamaha factsheet URLs.
- **Redirect loops:** kawasaki.com (US). content.kawasaki.com did not resolve.
- **Did not resolve or refused connection:** bajaj.com.tr, bajajturkiye.com, shop-motorrad.suzuki.de.
- **Timeouts:** bmw-motorrad.com.tr, nseindia archive PDF.
- **Broken TLS certificate:** sym.com.tr, so no SYM records.
- **Archives now returning 404:** kawasaki.eu / kawasaki.co.uk model archives; old KTM model-year pages (most redirect to the range page); legacy HMSI press-release URLs.
- **Unreliable source:** hawaii.kawasaki.com spec PDFs printed wrong data for older years (2018 Ninja 400 sheet showed 649 cc). Those sheets were not used.
- **Process note:** one research pass sent about 20 `curl -I` HEAD requests to check whether Yamaha factsheet URLs existed. No file bodies were downloaded. All content was read through WebFetch, and saved PDFs were read with pdftotext.

## Terms and permission observations

- Only facts were recorded: names, cc, model years, engine layout, transmission. No images, prices, descriptions or marketing text were copied.
- No page that was fetched showed explicit terms on reusing specification data. Sites carry standard copyright footers. Yamaha factsheets say "specifications subject to change without notice".
- hondanews.eu and BMW PressClub are press sites intended for media use. Their terms pages were not reviewed.
- Recommendation: before any public display of sourced data, someone should read the terms pages of the main sources (hondanews.eu, yamaha-motor.eu, kawasaki.eu, ktm.com). Plain spec facts are generally not copyrightable, but the database/terms position was not legally assessed.

## Records left unverified or weak, and why

- **LOW (5):**
  - Honda Activa 125 first generation (2014): secondary sources only, and HMSI archive URLs return 404.
  - Honda Dio 110: no India-official cc or year.
  - KTM 250 Duke before 2024: 248.8 cc from secondary sources only.
  - KTM RC 390 first generation: cc and years not official, although KTM confirms a previous generation existed.
  - TVS Jupiter 110 old generation: only a nominal "110 cc" is official.
- **MEDIUM with no year evidence:** CFMOTO 250NK and 450MT, Kymco (3), Mondial Fuzion 125, RKS Wildcat, Benelli TRK 702, TVS NTORQ 125, Bajaj Pulsar N250, BMW G 310 R facelift, and Yamaha NMAX 125 first generation (only an owner's manual).
- **Generations not recorded:**
  - Honda PCX125 before 2018, NC700X 2012-2013, CB650R 2019-2023
  - KTM 390 Duke 2013-2016 first generation
  - Yamaha XMAX 250 before 2018
- **Mid-cycle updates flagged but not split** (bodywork and instrument parts may differ):
  - Kawasaki Z650 2020, Z900 2020
  - Yamaha R25 EU5
  - Honda NC750X 2018, ADV350 25YM
  - Bajaj NS200 2023 redesign
  - TVS Apache RTR 200 4V 2025 "Next-Gen"
- **India 2026 GST downsizing:** Dominar 400 to 349.13 cc and KTM 390 Duke India to 349.32 cc. These are separate IN records and not interchangeable with TR/EU parts.
- **Easy next additions** (sources already located): Kawasaki Z500, CFMOTO 450SR / 450NK / 700CL-X / 800MT (TR pages), BMW G 310 GS, Yamaha MT-125 (has a 124 vs 125 conflict), XMAX 125, Bajaj Dominar 250.
- **Fetch tool caveat:** WebFetch summarizes pages with a model, so key values were re-checked with "quote exactly" prompts. The final review spot-checked three records directly, and all matched:
  - KTM 390 Duke 2026: 398.7 cm³
  - Suzuki Address 110 2015: 112 cm³
  - Kymco Downtown GT 350: 321 vs 320
  - A browser spot-check of HIGH records before production import is still advisable.
