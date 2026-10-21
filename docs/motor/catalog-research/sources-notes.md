# MOTOR - catalogue source register: notes and recommendations

Accessed 2026-10-19. The machine-readable register is in `sources.json` (43 entries).

**Baseline:** MOTOR has no known data, feed or image agreement with any source, so no source is `AUTHORIZED`. Everything below is something MOTOR would have to request. Every "terms" statement is a summary of a public page (not legal advice). Where a page could not be read, the register marks the terms as unverified (`termsUrl: null`) instead of guessing.

## Status overview

| Status | Count | Examples |
|---|---|---|
| RESTRICTED (terms explicitly forbid commercial reuse or scraping) | 4 | Honda Türkiye site, KTM legal notice, Brembo Parts, Yamaha Motor global terms |
| PERMISSION_REQUIRED | 22 | Hiflofiltro, EBC, Niterra/NGK, JT, DID, RK, Yuasa, DENSO, TecDoc, Part Smart, Icecat, Turkish B2B wholesalers |
| PUBLIC_FACTUAL_REFERENCE_ONLY | 11 | Turkish OEM distributors (Kawasaki/Z-Moto, Suzuki/Doğan Trend, Kuralkan/Bajaj, CFMOTO, SYM/Motoran, K-Rides, RKS, Mondial/UMA) |
| REJECTED | 6 | Competitor retailers, marketplaces, Partzilla, MSP, unofficial EPC mirrors |

## (a) Authorised product feeds with TRY prices and stock: whom to approach first

1. **TDK MOTO 60 (tdkmoto60.com/xml-bayilik).** This is the only motorcycle-specific Turkish wholesaler found that publicly documents an XML dealer feed. The feed has product, price and stock data for 48,000+ items and costs 70,000 TL/year. Before signing, get these in writing:
   - the legal entity name
   - the image-use clause
   - whether fitment fields exist (the page does not mention them)
   - which brands are officially sourced
   - SLA and update frequency
2. **Motosiklet Garajı (b2b.motosikletgaraji.com).** It runs a B2B dealer portal and claims to be authorised main dealer for Hiflo, EBC and BS Battery. Ask whether it offers an XML/CSV export. Verify its authorisation claim with Bike Alert plc (Hiflo) and EBC directly.
3. **Ufuk Motosiklet** (B2B, wholesale since 1979, 3,000 TL minimum order) and **Moto Spartan** (importer and wholesaler with dealer programme; also SYM/KYMCO/Mondial OEM parts). Neither documents an XML feed, so ask.
4. **Merpa Motor and Motolinas.** Both have B2B or dealer channels but little public information. Treat them as secondary.
5. **OEM distributors' dealer channels** (Honda Türkiye A.Ş., Z-Moto/Kawasaki, Doğan Trend/Suzuki, Kuralkan/Bajaj, Motoran/SYM, K-Rides). These are the only path to genuine OEM parts with official TRY prices. They usually require an authorised dealer or parts-reseller contract. Open the conversation through their public contact pages.

Benchmark: a non-motorcycle Turkish XML programme (Akyedek, car parts) charges a 20,000 TL one-time fee and enforces minimum resale prices. Expect similar MAP clauses.

## (b) Reseller image rights: whom to approach first

1. **Niterra EMEA (NGK/NTK).** This is the only brand found with a published reseller image licence. The MediaBox basic licence is free and non-transferable, valid in EMEA, and only for labelling Niterra products with a Niterra reference. The conditions are:
   - send website screenshots to Niterra before launch
   - do not modify the images
   - do not sublicense them
   - the licence can be revoked

   Start here. It is the lowest-friction "yes".
2. **Hiflofiltro / Bike Alert plc.** It publicly hosts a full product-photo ZIP (2026-06-05) plus logos, but states no licence. Ask for one written confirmation that authorised resellers may use the photos. This is likely quick.
3. **EBC Brakes (Freeman Automotive UK).** Its catalogue notice reserves photos and part numbers and requires express permission. Ask EBC sales and identify the official Turkish channel.
4. **JT Sprockets** (via its Dealer Signup page), **Yuasa** (GS Yuasa Battery Europe), **DID Europe**, **RK Europe SAS** and **DENSO Aftermarket Europe**. No image terms were found for any of them, so ask each.
5. **Do not ask Brembo, Honda TR, KTM or Yamaha for blanket web image use.** Their terms restrict it. Get images only through an authorised dealer agreement or their official press/media portals under the stated terms.

## (c) Fitment data licensing: whom to approach first

1. **Brand fitment directly from aftermarket manufacturers.** These brands already maintain motorcycle fitment lookups:
   - Hiflofiltro (catalogue plus Application Guide PDF)
   - EBC (UK MC catalogue 205, Harley 406)
   - JT Sprockets
   - NGK (2-wheel part finder)
   - Yuasa (battery lookup)

   Ask each for a bulk application file (CSV/XLS) under a reseller agreement. This is cheaper and more motorcycle-accurate than an aggregator.
2. **TecAlliance TecDoc.** It has a formal commercial licence (GTC v3.3, 01.10.2026: non-transferable, project-scoped, anti-copying measures). Motorcycle coverage started at about 4,000 models and still sits under the passenger-car structure. Ask for a two-wheel coverage report for the Turkish parc before paying. Brembo and NGK car data already flow through TecDoc.
3. **Part Smart / Part Stream (ARI).** This is OEM illustrated-fiche licensing, but it is North-America-centric and normally tied to authorised OEM dealer status. Only pursue it if MOTOR becomes an authorised dealer.
4. **Icecat (Open Icecat).** It has a free API under the Open Content License, but it is not a fitment source and its motorcycle-parts brand coverage is unverified. Use it only for generic products (helmets, apparel, tools, batteries) if those brands participate.
5. **Not suitable:**
   - MOTOR Information Systems (no motorcycle coverage evidenced)
   - Partzilla, MSP and other OEM-fiche retailers (competitors)
   - unofficial EPC mirrors

## Key caveats

- **Distribution in Türkiye is in flux.** On 2026-10-07, Doğan Trend announced it is ending Kymco and Piaggio-group (Vespa, Aprilia, Moto Guzzi, Piaggio) motorcycle operations and keeping Suzuki. KTM's current Türkiye importer could not be verified (the KTM group is restructuring under Bajaj). CFMOTO TR is probably UMA (Uğur Motorlu Araçlar), but this is not confirmed. No TVS distributor was found.
- **"Mondial" is two different brands.** The Turkish budget brand Mondial is linked to Uğur Motorlu Araçlar A.Ş. The Italian F.B. Mondial is distributed by K-Rides (Kibar).
- **No official Türkiye importer was found** for Hiflo, EBC, NGK, DID, RK, JT, Brembo or Yuasa. Turkish sellers' "yetkili / ana bayi" claims are self-reported.
- **Some pages could not be read.** Pages rendered client-side or blocked returned no legal text: yamaha-motor.eu, kuralkan.com.tr (403), partzilla.com (403) and mondial.com.tr. Re-check those manually in a browser.
- **Some URLs are unverified.** The URLs for DID, RK, Part Smart and TVS were not fetched, so verify them before outreach.
- **Facts versus content.** Model names, years and displacement are facts that can be referenced. Copying spec tables, descriptions, prices or images from OEM and distributor sites is not covered by any licence found.

## Permission request e-mail template (Turkish)

> **Konu:** Ürün verisi ve görsel kullanım izni talebi – [MOTOR TİCARİ ÜNVANI]
>
> Sayın [MUHATAP ADI / Firma Yetkilisi],
>
> [MOTOR TİCARİ ÜNVANI] olarak [WEB ADRESİ] üzerinden Türkiye'de motosiklet yedek parça ve aksesuarlarını TL fiyatlarla satışa sunan bir e-ticaret mağazası işletiyoruz (Vergi No: [VERGİ NO], Adres: [ADRES]). [MARKA / FİRMA ADI] ürünlerini müşterilerimize doğru ve güncel bilgilerle sunmak istiyoruz. Bu nedenle aşağıdaki konularda yazılı izninizi ve varsa koşullarınızı öğrenmek istiyoruz:
>
> 1. Ürün verileri (parça numarası, teknik özellikler, motosiklet uyumluluk/fitment bilgisi) için toplu veri dosyası (XML/CSV/API) erişimi;
> 2. Ürün görsellerinin ve logolarının web sitemizde ve pazaryeri mağazalarımızda yetkili satıcı olarak kullanılması;
> 3. Bayi/yetkili satıcı fiyat ve stok bilgisine (TL) erişim ve bunun için gerekli başvuru veya sözleşme koşulları.
>
> Verileri yalnızca [MARKA] ürünlerinin satışı amacıyla, kaynak göstererek ve değiştirmeden kullanacağız. Talep etmeniz hâlinde yayın öncesi ekran görüntülerini onayınıza sunabiliriz. Uygun bir sözleşme veya lisans metniniz varsa iletmenizi rica ederiz. İzin verilmediği sürece içeriklerinizi kullanmayacağız.
>
> Saygılarımızla,
> [AD SOYAD] – [UNVAN]
> [MOTOR TİCARİ ÜNVANI] | [TELEFON] | [E-POSTA] | [WEB ADRESİ]
