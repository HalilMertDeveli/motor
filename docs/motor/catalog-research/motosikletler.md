# Motosiklet referans kayıtları (2026-10)

Kaynak dosya: `src/Smartstore.Modules/MotorShop.CatalogImport/Research/2026-10/vehicles.json` — 92 kayıt, her nesil ayrı kayıt.
Her kayıt, alanlarını destekleyen resmî kaynak URL'lerini (`sources[].supports`) taşır. Ayrıntılı araştırma günlüğü: [vehicles-notes.md](vehicles-notes.md).

"Seçici yılları", modülün seçiciye yazacağı aralıktır: resmî kaynakta belirtilen başlangıç yılı ile doğrulanmış en son model yılı
arası. Üretim sonu resmî olarak belirtilmediyse tahmin edilmez. 71 kayıt seçiciye eklenebilir; diğerleri referans olarak kalır.

## Marka özeti

| Marka | Kayıt | HIGH | MEDIUM | LOW | Seçiciye eklenebilir |
|---|---:|---:|---:|---:|---:|
| Honda | 23 | 19 | 2 | 2 | 20 |
| Yamaha | 20 | 12 | 8 | 0 | 19 |
| KTM | 11 | 6 | 3 | 2 | 8 |
| Kawasaki | 8 | 6 | 2 | 0 | 8 |
| Suzuki | 8 | 5 | 3 | 0 | 8 |
| Bajaj | 5 | 2 | 3 | 0 | 4 |
| TVS | 4 | 2 | 1 | 1 | 0 |
| BMW | 3 | 2 | 1 | 0 | 2 |
| Kymco | 3 | 0 | 3 | 0 | 0 |
| CFMOTO | 2 | 0 | 2 | 0 | 0 |
| Triumph | 2 | 2 | 0 | 0 | 2 |
| Benelli | 1 | 0 | 1 | 0 | 0 |
| Mondial | 1 | 0 | 1 | 0 | 0 |
| RKS | 1 | 0 | 1 | 0 | 0 |

## Başlangıç (seed) listesinin denetimi

MotorShop.Fitment'in örnek listesindeki 35 kaydın karşılığı. Bir seed kaydı birden çok nesle bölündüyse her nesil ayrı satırdır.

| Seed kaydı | Sonuç | Doğrulanmış kayıt | Ayrıntı |
|---|---|---|---|
| Bajaj Dominar 400 2017-2025 373 | VERIFIED | Bajaj Dominar 400 | TR distributor spec gives 373 cc and Bajaj gives 373.3 cc (rounding). Bajaj confirms the 2017 launch and 2021 and 2025 models. The India-only 349.13 cc engine from April 2026 is outside the seed range and has a separate record. |
| Bajaj Pulsar NS200 2012-2025 199 | CORRECTED | Bajaj Pulsar NS200 | The official cc is 199.5, not 199. The range is not contradicted, but the 2012 start is secondary-only. A 2023 front-end update is noted. |
| Bajaj Pulsar RS200 2015-2025 199 | CORRECTED | Bajaj Pulsar RS200 | The official cc is 199.5, not 199. The 2015 start and a 2025 model year are officially confirmed. |
| Honda Activa 125 2014-2025 124 | CORRECTED | Honda Activa 125 | Secondary sources show two engine generations: 2014 BS-IV (reported as 124.9cc) and 2019 BS-VI FI (official current figure 123.92cc). The seed range merges them. The BS-IV generation could not be verified officially. |
| Honda Activa 125 2014-2025 124 | CORRECTED | Honda Activa 125 | The official cc is 123.92 (the seed's 124 is a rounding). The 2014-2025 seed range spans at least two engine generations (BS-IV and BS-VI FI). |
| Honda CB 125F 2015-2025 124 | CORRECTED | Honda CB125F | The seed has to be split. The 2015-2020 generation is 124.7cc (seed: 124). The all-new 21YM bike is 124cc, and the 26YM model is another restyle. |
| Honda CB 125F 2015-2025 124 | CORRECTED | Honda CB125F | This is the seed's 124cc generation. The seed range is split: 2015-2020 is 124.7cc, 2021-2025 is 124cc, and 2026+ is a restyle. |
| Honda CB 125F 2015-2025 124 | CORRECTED | Honda CB125F | This generation is outside the seed range (26YM). It is listed so the 2021-2025 record has a documented end. |
| Honda CBR 250R 2011-2013 250 | CORRECTED | Honda CBR250R | The official cc is 249.6 (seed: 250). Honda Europe lists the model as 2011-2014, so the seed's 2013 end year is contradicted for the EU market. |
| Honda CBR 650R 2019-2025 649 | CORRECTED | Honda CBR650R | The cc of 649 is verified. The range has to be split at the 24YM update (new bodywork, lighting and TFT, optional E-Clutch), giving 2019-2023 and 2024+. |
| Honda CBR 650R 2019-2025 649 | CORRECTED | Honda CBR650R | This is the second record from the split seed. The cc of 649 is verified, and this generation starts with 24YM. |
| Honda CRF1100L Africa Twin 2020-2025 1084 | CORRECTED | Honda CRF1100L Africa Twin | The cc of 1084 and the 2020 start are verified. The range is split at 24YM because of the engine internals retune (compression, valve timing, intake) and the tubeless wheels. |
| Honda CRF1100L Africa Twin 2020-2025 1084 | CORRECTED | Honda CRF1100L Africa Twin | The second record from the split seed. The cc of 1084 is verified. |
| Honda Dio 110 2019-2025 110 | UNVERIFIABLE | Honda Dio 110 | No India-official cc or year was found. A third-market official distributor gives 109.51cc (the seed's 110 is the class name). Nothing official supports the seed's 2019 start. Secondary sources point to a BS-VI generation from 2020, and the Dio name in India goes back much further. |
| Honda Forza 250 2018-2025 249 | VERIFIED | Honda Forza 250 | Honda Türkiye gives 249 cc, which matches the seed. No official source contradicts 2018-2025, and a TR launch in Aug 2018 is supported only by secondary press. Generation boundaries inside 2018-2025 are unverified (a likely restyle), so treat the range as one record with caution. The Forza 250 is sold in Türkiye, while the EU line-up is Forza 125/350. |
| Honda NC 750X 2014-2025 745 | CORRECTED | Honda NC750X | The cc of 745 is correct for every generation since 2014, but the seed range has to be split into 2014-2015, 2016-2020, 2021-2024 (TBW, new frame/bodywork, more power) and 2025+ (dual front discs, new wheels, TFT). The 2012-2013 NC700X is 670cc and a different model. |
| Honda NC 750X 2014-2025 745 | CORRECTED | Honda NC750X | One of the four generations split out of the seed range. The cc of 745 is verified. |
| Honda NC 750X 2014-2025 745 | CORRECTED | Honda NC750X | The 2021 engine update generation split out of the seed range. The cc of 745 is verified. |
| Honda NC 750X 2014-2025 745 | CORRECTED | Honda NC750X | The current generation split out of the seed range. The cc of 745 is verified. |
| Honda PCX 125 2010-2025 125 | CORRECTED | Honda PCX125 | The cc of 125 is verified, but the seed spans several generations. Honda Europe history: introduced in 2010, eSP engine in 2012, EURO4 in 2016, new frame in 2018, new 4-valve eSP+ engine and frame in 2021, and a 2025 update (rear disc, restyle). Only 2018+ is recorded. The pre-2018 generations still need research. |
| Honda PCX 125 2010-2025 125 | CORRECTED | Honda PCX125 | This is one of the generations split out of the seed range. The cc of 125 is verified. |
| Honda PCX 125 2010-2025 125 | CORRECTED | Honda PCX125 | This is the current generation split out of the seed range. The cc of 125 is verified. |
| Kawasaki Ninja 250R 2008-2012 249 | VERIFIED | Kawasaki Ninja 250R | 2008 launch and replacement by the Ninja 300 / new Ninja 250 for 2013 are officially stated, matching 2008-2012. 249 cm3 is the EU figure; Kawasaki Japan's 2008 release says 248 cc, so confidence is MEDIUM. Later Ninja 250 (2013+, 249 cm3) and Ninja 300 (296 cm3) are different models, not this record. |
| Kawasaki Ninja 400 2018-2025 399 | VERIFIED | Kawasaki Ninja 400 | 399 cm3 and the 2018 start are officially confirmed. Caveat: in EU/US/TR the Ninja 400 is effectively replaced by the Ninja 500 from MY2024 (no official EU Ninja 400 page after 2023 found), so a 2025 end year is not supported for these markets, though no official end year is stated either. See separate Ninja 500 record. |
| Kawasaki Versys 650 2015-2025 649 | CORRECTED | Kawasaki Versys 650 | 649 cm3 and the 2015 start are confirmed, but the range spans the 2015 generation and the 2022 update (new bodywork/electronics, likely different model code). Split into 2015-2021 and 2022+ records. |
| Kawasaki Versys 650 2015-2025 649 | CORRECTED | Kawasaki Versys 650 | Split record: 2022+ portion of the seed range. |
| Kawasaki Z650 2017-2025 649 | VERIFIED | Kawasaki Z650 | 649 cm3 and the 2017 start are officially confirmed; 2025 range not contradicted (2025 EU page exists in index, 2026 EU/TR pages fetched). Note the 2020 mid-cycle update. |
| Kawasaki Z900 2017-2025 948 | CORRECTED | Kawasaki Z900 | 948 cm3 confirmed, but the seed range spans two generations: 2017-2024 (this record) and the remodeled 2025+ Z900 (electronic throttle, new bodywork/brakes, separate record). |
| Kawasaki Z900 2017-2025 948 | CORRECTED | Kawasaki Z900 | Split record: the 2025 model year of the seed range belongs to this remodeled generation. |
| KTM 125 Duke 2011-2025 125 | CORRECTED | KTM 125 Duke | Generations must be split: the 2024+ model has a new engine (124.99 cc) and the pre-2024 model has 124.7 cc. The seed value of 125 is a nominal figure. The 2011 start year and the 2017/2021 updates could not be checked officially. |
| KTM 125 Duke 2011-2025 125 | CORRECTED | KTM 125 Duke | Split off as a separate generation: new engine of 124.99 cc from 2024. |
| KTM 250 Duke 2015-2025 249 | CORRECTED | KTM 250 Duke | Must be split: the 2024+ Gen-3 model has a new engine (249.07 cc per KTM) and the pre-2024 model has 248.8 cc (secondary only). The 2015 start year was not checked officially. |
| KTM 250 Duke 2015-2025 249 | CORRECTED | KTM 250 Duke | Split off as a separate generation from 2024 (new engine, 249.07 cc). |
| KTM 390 Adventure 2020-2025 373 | CORRECTED | KTM 390 Adventure | The cc is confirmed as 373.2 (seed says 373). The seed range is contradicted: MY2025 brought a new generation with a 398.7 cc LC4c engine (separate record). The 2020 start was not checked officially. |
| KTM 390 Duke 2013-2025 373 | CORRECTED | KTM 390 Duke | The range is contradicted: from MY2024 the 390 Duke is the Gen-3 with a 398.7 cc engine (separate record). 373 cc is confirmed only for the pre-2024 model (2023 verified). The 2013 start and 2017 Gen-2 split are secondary-only. |
| KTM RC 390 2014-2025 373 | CORRECTED | KTM RC 390 | Must be split at MY2022 (new generation per the KTM press kit). For this first generation, neither the 2014 start nor the cc was confirmed officially. |
| KTM RC 390 2014-2025 373 | CORRECTED | KTM RC 390 | The cc of 373 matches, but the seed range covers two generations. This record is the MY2022+ generation. |
| Suzuki Address 110 2015-2025 113 | CORRECTED | Suzuki Address 110 | Official spec for the 2015 4-stroke Address 110 is 112 cm3; 113 cm3 is the 1998 two-stroke model. 2015 start confirmed. 2025 end not evidenced: the scooter is absent from the Suzuki global product index and Suzuki Türkiye line-up (Address 125 shown), and an unverified Suzuki DE parts listing suggests MY 2015-2021. |
| Suzuki Burgman 400 2017-2025 400 | VERIFIED | Suzuki Burgman 400 | 2017 'all-new' generation confirmed; model years 2024-2027 on Suzuki global. 400 matches the TR/UK figure, but launch materials state 399 cm3 (MEDIUM). |
| Suzuki GSX-R125 2017-2025 124 | VERIFIED | Suzuki GSX-R125 | Single generation since the 2017 launch; years 2018-2027 officially evidenced. Seed 124 matches the launch figure; current TR/global figure is 124.4 cm3 (recorded). |
| Suzuki V-Strom 650 2012-2025 645 | CORRECTED | Suzuki V-Strom 650 | 645 cm3 confirmed, but the range spans two generations: 2012-2016 (this record) and the all-new 2017+ model. Split. |
| Suzuki V-Strom 650 2012-2025 645 | CORRECTED | Suzuki V-Strom 650 | Split record: the 2017 all-new generation. 2025 model year is officially evidenced; GB lists it as no longer in the new range. |
| TVS Apache RTR 200 4V 2016-2025 197 | CORRECTED | TVS Apache RTR 200 4V | The official cc is 197.75, not 197. The 2016 launch and a 2025 model are officially confirmed. A possible 2025 'next-gen' chassis change is unresolved. |
| TVS Jupiter 110 2013-2025 110 | CORRECTED | TVS Jupiter 110 | Must be split: TVS launched an all-new Jupiter 110 with a new 113.3 cc engine on 22 Aug 2024 (separate record). For the older model, only a nominal 110 cc is official and the 2013 start is secondary-only. |
| TVS Jupiter 110 2013-2025 110 | CORRECTED | TVS Jupiter 110 | New generation from Aug 2024 with a 113.3 cc engine, split from the older model. |
| TVS Ntorq 125 2018-2025 124 | CORRECTED | TVS NTORQ 125 | The official cc is 124.8, not 124. The year range is not contradicted, but the 2018 start is secondary-only. |
| Yamaha MT-07 2014-2025 689 | CORRECTED | Yamaha MT-07 | cc 689 confirmed in every EU spec table (2014-2025) and the 2025 tr-TR factsheet. Seed must be split into four generations: 2014 launch, 2018 update (new body, revised suspension), 2021 next generation (EU5, LED face), 2025 latest evolution (new styling, chassis updates, 41 mm USD fork, Y-AMT option). |
| Yamaha MT-07 2014-2025 689 | CORRECTED | Yamaha MT-07 | cc 689 confirmed in every EU spec table (2014-2025) and the 2025 tr-TR factsheet. Seed must be split into four generations: 2014 launch, 2018 update (new body, revised suspension), 2021 next generation (EU5, LED face), 2025 latest evolution (new styling, chassis updates, 41 mm USD fork, Y-AMT option). |
| Yamaha MT-07 2014-2025 689 | CORRECTED | Yamaha MT-07 | cc 689 confirmed in every EU spec table (2014-2025) and the 2025 tr-TR factsheet. Seed must be split into four generations: 2014 launch, 2018 update (new body, revised suspension), 2021 next generation (EU5, LED face), 2025 latest evolution (new styling, chassis updates, 41 mm USD fork, Y-AMT option). |
| Yamaha MT-07 2014-2025 689 | CORRECTED | Yamaha MT-07 | cc 689 confirmed in every EU spec table (2014-2025) and the 2025 tr-TR factsheet. Seed must be split into four generations: 2014 launch, 2018 update (new body, revised suspension), 2021 next generation (EU5, LED face), 2025 latest evolution (new styling, chassis updates, 41 mm USD fork, Y-AMT option). |
| Yamaha MT-25 2015-2025 249 | CORRECTED | Yamaha MT-25 | Seed merges two generations. 1st gen: 249 cm3 in EU factsheets 2016/2018 (matches seed). 2020 'next generation MT-25' (37 mm upside-down fork, new face) is a separate record whose factsheets mostly state 250 (2020, 2024, 2025, 2025 tr-TR) but 249cc in 2022. |
| Yamaha MT-25 2015-2025 249 | CORRECTED | Yamaha MT-25 | Part of split seed; see 1st gen record. |
| Yamaha NMAX 125 2015-2025 125 | CORRECTED | Yamaha NMAX 125 | cc 125 confirmed (owner's manual 125 cm3; EU factsheets 2021-2025 and 2025 tr-TR 125cc). Seed must be split: original GPD125-A generation (owner's manual 1st edition April 2015), 2021 'all-new NMAX 125' (all-new frame and body), 2025 'latest generation' (compact new body, dual-eye headlights, EU5+). |
| Yamaha NMAX 125 2015-2025 125 | CORRECTED | Yamaha NMAX 125 | cc 125 confirmed (owner's manual 125 cm3; EU factsheets 2021-2025 and 2025 tr-TR 125cc). Seed must be split: original GPD125-A generation (owner's manual 1st edition April 2015), 2021 'all-new NMAX 125' (all-new frame and body), 2025 'latest generation' (compact new body, dual-eye headlights, EU5+). |
| Yamaha NMAX 125 2015-2025 125 | CORRECTED | Yamaha NMAX 125 | cc 125 confirmed (owner's manual 125 cm3; EU factsheets 2021-2025 and 2025 tr-TR 125cc). Seed must be split: original GPD125-A generation (owner's manual 1st edition April 2015), 2021 'all-new NMAX 125' (all-new frame and body), 2025 'latest generation' (compact new body, dual-eye headlights, EU5+). |
| Yamaha Tracer 7 2021-2025 689 | CORRECTED | Yamaha Tracer 7 | cc 689 confirmed (EU factsheets 2022, 2024, 2025 and 2025 tr-TR). Seed must be split: TRACER 7 name from MY2021 on the bodywork introduced for MY2020 as 'new Tracer 700'; MY2025 'new TRACER 7' with revised chassis, longer swingarm, USD fork, 18 L tank. |
| Yamaha Tracer 7 2021-2025 689 | CORRECTED | Yamaha Tracer 7 | cc 689 confirmed (EU factsheets 2022, 2024, 2025 and 2025 tr-TR). Seed must be split: TRACER 7 name from MY2021 on the bodywork introduced for MY2020 as 'new Tracer 700'; MY2025 'new TRACER 7' with revised chassis, longer swingarm, USD fork, 18 L tank. |
| Yamaha XMAX 250 2014-2025 249 | CORRECTED | Yamaha XMAX 250 | No official evidence found for a 2014-2017 XMAX 250 (no factsheet located; seed start 2014 unverified). EU factsheets 2018/2020/2023 state 249cc, but 2024 tr-TR and 2025 EN state 250cc. Split into 2018 generation (new Blue Core engine) and 2023 update (new body design, X-shaped lighting). |
| Yamaha XMAX 250 2014-2025 249 | CORRECTED | Yamaha XMAX 250 | No official evidence found for a 2014-2017 XMAX 250 (no factsheet located; seed start 2014 unverified). EU factsheets 2018/2020/2023 state 249cc, but 2024 tr-TR and 2025 EN state 250cc. Split into 2018 generation (new Blue Core engine) and 2023 update (new body design, X-shaped lighting). |
| Yamaha YZF-R25 2014-2025 249 | CORRECTED | Yamaha YZF-R25 | Seed merges three generations and uses 249. EU/TR factsheets (2015-2025, incl. 2025 tr-TR) all state 250cc; only the 2014 Yamaha global release states 249cm3. Split into 2014 gen, 2019 update (new bodywork, 37 mm inverted fork) and 2025 update (new look for 2025). Seed start 2014 matches the official launch; end 2025 is not a stated end. |
| Yamaha YZF-R25 2014-2025 249 | CORRECTED | Yamaha YZF-R25 | Part of split seed; see 1st gen record. |
| Yamaha YZF-R25 2014-2025 249 | CORRECTED | Yamaha YZF-R25 | Part of split seed; see 1st gen record. |

## Tüm kayıtlar

| Marka | Model | Varyant | Nesil | Yıl aralığı (kaynak) | Doğrulanmış model yılları | Seçici yılları | cc | Kategori | Pazar | Güven | Durum | Kaynak sayısı |
|---|---|---|---|---|---|---|---:|---|---|---|---|---:|
| Bajaj | Dominar 400 | — | 373 cc engine (2017+; still sold in TR) | 2017–— | 2017, 2021, 2025 | 2017–2025 | 373 | touring | TR | MEDIUM | Seçiciye eklenebilir | 4 |
| Bajaj | Dominar 400 | India 350 cc engine | India 349.13 cc engine (Apr 2026+) | 2026–— | 2026 | 2026–2026 | 349.13 | touring | IN | MEDIUM | Başka pazar (referans) | 3 |
| Bajaj | Pulsar N250 | — | — | —–— | 2024 | 2024–2024 | 249.07 | naked | TR | MEDIUM | Seçiciye eklenebilir | 3 |
| Bajaj | Pulsar NS200 | — | — | —–— | 2023, 2024 | 2023–2024 | 199.5 | naked | TR | HIGH | Seçiciye eklenebilir | 5 |
| Bajaj | Pulsar RS200 | — | — | 2015–— | 2015, 2025 | 2015–2025 | 199.5 | sport | TR | HIGH | Seçiciye eklenebilir | 4 |
| Benelli | TRK 702 | — | — | —–— | — | — | 698 | adventure | global | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| BMW | G 310 R | — | 1st gen (2015 press kit, pre-EU5 facelift) | —–— | 2020 | 2020–2020 | 313 | naked | global | HIGH | Seçiciye eklenebilir | 3 |
| BMW | G 310 R | — | EU5 facelift (presented 2020-11-19) | —–— | — | — | 313 | naked | global | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| BMW | R 1300 GS | — | R 1300 GS (first generation, successor of R 1250 GS) | 2024–— | 2024 | 2024–2024 | 1300 | adventure | global | HIGH | Seçiciye eklenebilir | 1 |
| CFMOTO | 250NK | — | — | —–— | — | — | 249 | naked | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| CFMOTO | 450MT | — | — | —–— | — | — | 449 | adventure | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Honda | Activa 125 | — | 1st gen BS-IV (2014 launch, carburettor) | —–— | — | — | — | scooter | IN | LOW | Başka pazar (referans) | 2 |
| Honda | Activa 125 | — | BS-VI PGM-FI generation (OBD2B update for 2025) | —–— | 2025 | 2025–2025 | 123.92 | scooter | IN | MEDIUM | Başka pazar (referans) | 2 |
| Honda | ADV350 | — | 1st gen (22YM introduction; 25YM update) | 2022–— | 2022, 2026 | 2022–2026 | 330 | scooter | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | CB125F | — | 1st gen 2015-2020 (replaced CBF125) | 2015–— | 2015 | 2015–2015 | 124.7 | commuter | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | CB125F | — | 2nd gen 2021-2025 (new eSP engine and frame) | 2021–— | 2021, 2025 | 2021–2025 | 124 | commuter | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | CB125F | — | 26YM update (restyle, TFT, Idling Stop, EURO5+) | 2026–— | 2026 | 2026–2026 | 124 | commuter | EU | HIGH | Seçiciye eklenebilir | 1 |
| Honda | CB500 Hornet | — | 1st gen (24YM, successor of CB500F) | 2024–— | 2024, 2025 | 2024–2025 | 471 | naked | TR | HIGH | Seçiciye eklenebilir | 3 |
| Honda | CB650R | — | 2024+ update (restyle, TFT, optional Honda E-Clutch) | 2024–— | 2024, 2025, 2026 | 2024–2026 | 649 | naked | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | CBR250R | — | 2011-2014 (EU single-cylinder CBR250R) | 2011–2014 | 2011, 2014 | 2011–2014 | 249.6 | sport | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | CBR650R | — | 1st gen 2019-2023 (replaced CBR650F) | 2019–— | 2019, 2023 | 2019–2023 | 649 | sport | EU | HIGH | Seçiciye eklenebilir | 3 |
| Honda | CBR650R | — | 2024+ update (restyle, TFT, optional Honda E-Clutch) | 2024–— | 2024, 2025, 2026 | 2024–2026 | 649 | sport | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | CRF1100L Africa Twin | — | 2020-2023 (1084cc, replaced 998cc CRF1000L) | 2020–— | 2020, 2023 | 2020–2023 | 1084 | adventure | EU | HIGH | Seçiciye eklenebilir | 3 |
| Honda | CRF1100L Africa Twin | — | 2024+ (retuned engine 112 Nm, tubeless wheels, ES variant) | 2024–— | 2024, 2025, 2026 | 2024–2026 | 1084 | adventure | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | Dio | 110 | — | —–— | — | — | 109.51 | scooter | IN | LOW | Başka pazar (referans) | 3 |
| Honda | Forza 250 | — | Türkiye-market Forza 250 (current) | —–— | 2026 | 2026–2026 | 249 | scooter | TR | MEDIUM | Seçiciye eklenebilir | 4 |
| Honda | NC750X | — | 2014-2015 (745cc, successor of NC700X) | 2014–— | 2014 | 2014–2014 | 745 | adventure | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | NC750X | — | 2016-2020 (adventure restyle, LED, EURO4) | 2016–— | 2016 | 2016–2016 | 745 | adventure | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | NC750X | — | 2021-2024 (revised engine, TBW, revised frame, new bodywork) | 2021–— | 2021, 2023 | 2021–2023 | 745 | adventure | EU | HIGH | Seçiciye eklenebilir | 3 |
| Honda | NC750X | — | 2025+ update (restyle, dual front discs, new wheels, TFT) | 2025–— | 2025, 2026 | 2025–2026 | 745 | adventure | TR | HIGH | Seçiciye eklenebilir | 4 |
| Honda | NX500 | — | 1st gen (24YM, successor of CB500X) | 2024–— | 2024, 2025 | 2024–2025 | 471 | adventure | TR | HIGH | Seçiciye eklenebilir | 3 |
| Honda | PCX125 | — | 2018-2020 (new duplex frame, 2-valve eSP) | 2018–— | 2018 | 2018–2018 | 125 | scooter | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | PCX125 | — | 2021-2024 (new 4-valve eSP+ engine, new frame) | 2021–— | 2021 | 2021–2021 | 125 | scooter | EU | HIGH | Seçiciye eklenebilir | 2 |
| Honda | PCX125 | — | 2025+ update (restyle, rear disc brake, EURO5+) | 2025–— | 2025, 2026 | 2025–2026 | 125 | scooter | TR | HIGH | Seçiciye eklenebilir | 3 |
| Kawasaki | Ninja 250R | — | 1st gen Ninja 250R (2008 launch) | 2008–— | 2008 | 2008–2008 | 249 | sport | EU | MEDIUM | Seçiciye eklenebilir | 5 |
| Kawasaki | Ninja 400 | — | 2018 generation (EX400) | 2018–— | 2018, 2020, 2023 | 2018–2023 | 399 | sport | EU | HIGH | Seçiciye eklenebilir | 5 |
| Kawasaki | Ninja 500 | — | 1st gen (2024+) | 2024–— | 2024, 2026, 2027 | 2024–2027 | 451 | sport | TR | HIGH | Seçiciye eklenebilir | 5 |
| Kawasaki | Versys 650 | — | 2015 restyle generation | 2015–— | 2015 | 2015–2015 | 649 | adventure | EU | HIGH | Seçiciye eklenebilir | 2 |
| Kawasaki | Versys 650 | — | 2022 update | 2022–— | 2022, 2026, 2027 | 2022–2027 | 649 | adventure | TR | HIGH | Seçiciye eklenebilir | 3 |
| Kawasaki | Z650 | — | 2017 generation (successor of ER-6n); updated 2020 | 2017–— | 2017, 2020, 2026 | 2017–2026 | 649 | naked | EU | MEDIUM | Seçiciye eklenebilir | 4 |
| Kawasaki | Z900 | — | 1st gen 2017-2024 (updated 2020) | 2017–— | 2017, 2020 | 2017–2020 | 948 | naked | EU | HIGH | Seçiciye eklenebilir | 4 |
| Kawasaki | Z900 | — | 2025 remodel (Refined Sugomi) | 2025–— | 2025, 2027 | 2025–2027 | 948 | naked | TR | HIGH | Seçiciye eklenebilir | 5 |
| KTM | 125 Duke | — | pre-2024 generation (124.7 cc engine) | —–— | 2023 | 2023–2023 | 124.7 | naked | EU | MEDIUM | Seçiciye eklenebilir | 2 |
| KTM | 125 Duke | — | 2024+ generation (new LC4c engine, new chassis) | 2024–— | 2026 | 2024–2026 | 124.99 | naked | EU | HIGH | Seçiciye eklenebilir | 2 |
| KTM | 250 Duke | — | pre-2024 generation | —–— | — | — | 248.8 | naked | global | LOW | Aday (yıl/cc doğrulanmadı) | 3 |
| KTM | 250 Duke | — | Gen-3 (2024+, LC4c engine) | 2024–— | 2025, 2026 | 2024–2026 | 249.07 | naked | global | HIGH | Seçiciye eklenebilir | 4 |
| KTM | 390 Adventure | — | 1st generation (373.2 cc) | —–— | 2021, 2023, 2024 | 2021–2024 | 373.2 | adventure | global | HIGH | Seçiciye eklenebilir | 4 |
| KTM | 390 Adventure | R / X | 2nd generation (MY2025+, LC4c 399 cc engine) | 2025–— | 2025, 2026 | 2025–2026 | 398.7 | adventure | EU | HIGH | Seçiciye eklenebilir | 3 |
| KTM | 390 Duke | — | pre-2024 generation (373 cc; Gen-2 per Bajaj's later Gen-3 naming) | —–— | 2023 | 2023–2023 | 373 | naked | global | MEDIUM | Seçiciye eklenebilir | 3 |
| KTM | 390 Duke | — | Gen-3 (2024+, LC4c 399 cc engine, new frame) | 2024–— | 2026 | 2024–2026 | 398.7 | naked | EU | HIGH | Seçiciye eklenebilir | 3 |
| KTM | 390 Duke | India 350 cc version | India-only 350 cc engine variant (2026+) | 2026–— | 2026 | 2026–2026 | 349.32 | naked | IN | MEDIUM | Başka pazar (referans) | 2 |
| KTM | RC 390 | — | 1st generation (before the MY2022 redesign) | —–— | — | — | — | sport | global | LOW | Aday (yıl/cc doğrulanmadı) | 2 |
| KTM | RC 390 | — | 2nd generation (MY2022+, new frame, wheels, brakes, bodywork) | 2022–— | 2022, 2024 | 2022–2024 | 373 | sport | global | HIGH | Seçiciye eklenebilir | 3 |
| Kymco | Agility S 125i | — | — | —–— | — | — | 124.6 | scooter | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Kymco | AK 550 Premium | — | — | —–— | — | — | 550 | scooter | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Kymco | Downtown GT 350 | — | — | —–— | — | — | 321 | scooter | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Mondial | Fuzion 125 | — | — | —–— | — | — | 124 | scooter | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| RKS | Wildcat | — | — | —–— | — | — | 125 | scooter | TR | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Suzuki | Address 110 | — | 2015 4-stroke generation (Indonesia-built global model) | 2015–— | 2015 | 2015–2015 | 112 | scooter | global | MEDIUM | Seçiciye eklenebilir | 2 |
| Suzuki | Burgman 400 | — | 2017 generation (all-new Burgman 400) | 2017–— | 2017, 2024, 2025, 2026, 2027 | 2017–2027 | 400 | scooter | TR | MEDIUM | Seçiciye eklenebilir | 5 |
| Suzuki | Burgman Street 125EX | — | 1st gen (SEP-alpha engine) | 2023–— | 2023, 2024, 2026 | 2023–2026 | 124 | scooter | TR | HIGH | Seçiciye eklenebilir | 3 |
| Suzuki | GSX-8S | — | 1st gen (2023+) | 2023–— | 2023, 2024, 2025, 2026, 2027 | 2023–2027 | 776 | naked | TR | HIGH | Seçiciye eklenebilir | 3 |
| Suzuki | GSX-R125 | — | 1st gen (2017 launch) | 2017–— | 2018, 2020, 2023, 2024, 2025, 2027 | 2017–2027 | 124.4 | sport | TR | MEDIUM | Seçiciye eklenebilir | 4 |
| Suzuki | V-Strom 650 | — | 2nd gen (2012 full model change, V-Strom 650 ABS) | 2012–— | 2012, 2013 | 2012–2013 | 645 | adventure | EU | HIGH | Seçiciye eklenebilir | 3 |
| Suzuki | V-Strom 650 | — | 3rd gen (2017 all-new, incl. 650XT) | 2017–— | 2017, 2022, 2023, 2024, 2025 | 2017–2025 | 645 | adventure | EU | HIGH | Seçiciye eklenebilir | 4 |
| Suzuki | V-Strom 800DE | — | 1st gen (2023+) | 2023–— | 2023, 2024, 2025, 2026, 2027 | 2023–2027 | 776 | adventure | TR | HIGH | Seçiciye eklenebilir | 3 |
| Triumph | Speed 400 | — | — | —–— | 2024 | 2024–2024 | 398.15 | naked | global | HIGH | Seçiciye eklenebilir | 1 |
| Triumph | Trident 660 | — | — | —–— | 2025 | 2025–2025 | 660 | naked | EU | HIGH | Seçiciye eklenebilir | 1 |
| TVS | Apache RTR 200 4V | — | — | 2016–— | 2016, 2025 | 2016–2025 | 197.75 | naked | IN | HIGH | Başka pazar (referans) | 2 |
| TVS | Jupiter 110 | — | 1st generation (before the Aug 2024 all-new model) | —–— | — | — | — | scooter | IN | LOW | Başka pazar (referans) | 3 |
| TVS | Jupiter 110 | — | All-new Jupiter 110 (Aug 2024+, 113.3 cc engine) | 2024–— | 2024 | 2024–2024 | 113.3 | scooter | IN | HIGH | Başka pazar (referans) | 2 |
| TVS | NTORQ 125 | — | — | —–— | — | — | 124.8 | scooter | IN | MEDIUM | Başka pazar (referans) | 2 |
| Yamaha | MT-07 | — | 1st gen (2014 launch) | 2014–— | 2014, 2016, 2017 | 2014–2017 | 689 | naked | EU | HIGH | Seçiciye eklenebilir | 4 |
| Yamaha | MT-07 | — | 2nd gen / 2018 update (new body design, revised suspension) | 2018–— | 2018, 2020 | 2018–2020 | 689 | naked | EU | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | MT-07 | — | 3rd gen / 2021 next generation (EU5, Y-shape LED face) | 2021–— | 2021 | 2021–2021 | 689 | naked | EU | HIGH | Seçiciye eklenebilir | 1 |
| Yamaha | MT-07 | — | 4th gen / 2025 update (new styling, chassis updates, 41 mm USD fork, Y-AMT option) | 2025–— | 2025 | 2025–2025 | 689 | naked | TR | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | MT-25 | — | 1st gen (2015 launch, 41 mm telescopic fork) | 2015–— | 2016, 2018 | 2015–2018 | 249 | naked | EU | MEDIUM | Seçiciye eklenebilir | 3 |
| Yamaha | MT-25 | — | 2nd gen / 2020 next generation (new face, 37 mm upside-down fork) | 2020–— | 2020, 2022, 2024, 2025 | 2020–2025 | 250 | naked | TR | MEDIUM | Seçiciye eklenebilir | 5 |
| Yamaha | NMAX 125 | — | 1st gen (original GPD125-A) | —–— | — | — | 125 | scooter | EU | MEDIUM | Aday (yıl/cc doğrulanmadı) | 1 |
| Yamaha | NMAX 125 | — | 2nd gen / 2021 all-new (new frame and body, Blue Core EU5) | 2021–— | 2021, 2022, 2024 | 2021–2024 | 125 | scooter | EU | HIGH | Seçiciye eklenebilir | 3 |
| Yamaha | NMAX 125 | — | 3rd gen / 2025 latest generation (compact new body, dual-eye headlights, EU5+) | 2025–— | 2025 | 2025–2025 | 125 | scooter | TR | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | Ténéré 700 | — | 1st gen (2019) | —–— | 2019, 2022 | 2019–2022 | 689 | adventure | EU | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | Ténéré 700 | — | 2025 update (YCC-T electronic throttle, redesigned tank and bodywork, TFT) | 2025–— | 2025 | 2025–2025 | 689 | adventure | TR | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | Tracer 7 | — | 2021-gen (2020 redesign; Tracer 700 renamed TRACER 7 from MY2021) | 2021–— | 2021, 2022, 2024 | 2021–2024 | 689 | touring | EU | HIGH | Seçiciye eklenebilir | 4 |
| Yamaha | Tracer 7 | — | 2025 update (revised chassis, longer swingarm, 41 mm USD fork, Y-AMT option) | 2025–— | 2025 | 2025–2025 | 689 | touring | TR | HIGH | Seçiciye eklenebilir | 2 |
| Yamaha | XMAX 250 | — | 2018 generation (Blue Core engine) | 2018–— | 2018, 2020 | 2018–2020 | 249 | scooter | EU | MEDIUM | Seçiciye eklenebilir | 2 |
| Yamaha | XMAX 250 | — | 2023 update (new body design, X-shaped front lighting, 4.3-inch LCD) | 2023–— | 2023, 2024, 2025 | 2023–2025 | 250 | scooter | TR | MEDIUM | Seçiciye eklenebilir | 3 |
| Yamaha | XMAX 300 | — | 2023 update (new body design, X-shaped lighting, 4.3-inch LCD) | 2023–— | 2023 | 2023–2023 | 292 | scooter | EU | HIGH | Seçiciye eklenebilir | 1 |
| Yamaha | YZF-R25 | — | 1st gen (2014 launch, 41 mm telescopic fork) | 2014–— | 2015, 2016, 2017, 2018 | 2014–2018 | 250 | sport | EU | MEDIUM | Seçiciye eklenebilir | 5 |
| Yamaha | YZF-R25 | — | 2nd gen / 2019 update (new bodywork, 37 mm KYB inverted fork) | 2019–— | 2019, 2020, 2022, 2024 | 2019–2024 | 250 | sport | EU | MEDIUM | Seçiciye eklenebilir | 5 |
| Yamaha | YZF-R25 | — | 3rd gen / 2025 update (new twin-eye face, winglets, A&S clutch) | 2025–— | 2025 | 2025–2025 | 250 | sport | TR | MEDIUM | Seçiciye eklenebilir | 2 |
| Yamaha | YZF-R7 | — | 1st gen (2021 announcement, CP2 689 cc) | —–— | 2022, 2024 | 2022–2024 | 689 | sport | TR | HIGH | Seçiciye eklenebilir | 4 |
