# Yedek parça araştırma adayları (2026-10)

Kaynak dosya: `src/Smartstore.Modules/MotorShop.CatalogImport/Research/2026-10/parts.json` — 78 aday.
Uyumluluk satırlarının her biri parça üreticisinin kendi kataloğundan, o model ve yıllar için alınmıştır ve kaynak URL'si taşır.
Fiyat, stok, tedarikçi, görsel ve pazarlama metni **yoktur**. Ayrıntılar: [parts-notes.md](parts-notes.md).

Tüm adaylar kaynak kaydındaki bir kaynağa bağlıdır (`sourceKey`). Bu kaynakların hepsi şu an **PERMISSION_REQUIRED**
olduğundan parçalar araştırma adayı (A) olarak kalır; referans (B) veya satış (C) için kaynağın izin durumunun bir yönetici
tarafından güncellenmesi gerekir.

JT Sprockets kayıtları temel numarayı içerir; sipariş edilebilir numara diş sayısını da içerir (ör. temel + ".16").
Farklı diş sayıları farklı ürünler olduğundan bu kayıtlar KISMİ (PARTIAL) olarak işaretlendi.

## Özet

| Tür · Marka | Aday | Doğrulanmış | Kısmi |
|---|---:|---:|---:|
| oil-filter · Hiflofiltro | 6 | 5 | 1 |
| air-filter · Hiflofiltro | 10 | 9 | 1 |
| brake-pad · EBC Brakes | 20 | 16 | 4 |
| other · EBC Brakes | 1 | 1 | 0 |
| brake-disc · EBC Brakes | 7 | 6 | 1 |
| clutch · EBC Brakes | 4 | 4 | 0 |
| spark-plug · NGK | 20 | 13 | 7 |
| sprocket · JT Sprockets | 10 | 0 | 10 |

## Tüm adaylar

| Marka | Parça no (MPN) | Tür | Sınıf | Doğrulama | Güven | Üreticinin belirttiği uyumluluk | Kaynak | Eksik veri |
|---|---|---|---|---|---|---|---|---|
| Hiflofiltro | `HFA1623` | air-filter | AFTERMARKET | VERIFIED | HIGH | Honda CBR650R 2019–2025; Honda CBR650R 2026–2026 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA1715` | air-filter | AFTERMARKET | PARTIAL | MEDIUM | Honda NC750X 2014–2020 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA1935` | air-filter | AFTERMARKET | VERIFIED | HIGH | Honda CRF1100L Africa Twin 2020–2026 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA2406` | air-filter | AFTERMARKET | VERIFIED | HIGH | Kawasaki Ninja 400 2018–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA2610` | air-filter | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z650 2017–2024; Kawasaki Versys 650 2015–2020; Kawasaki Versys 650 2021–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA2922` | air-filter | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z900 2017–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA3611` | air-filter | AFTERMARKET | VERIFIED | HIGH | Suzuki V-Strom 650 2004–2025 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA4707` | air-filter | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2019; Yamaha MT-07 2020–2024; Yamaha MT-07 2025–2025; Yamaha Tracer 7 2016–2020; Yamaha Tracer 7 2021–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA6302` | air-filter | AFTERMARKET | VERIFIED | HIGH | KTM 125 Duke 2011–2016; KTM 250 Duke 2015–2016; KTM 390 Duke 2013–2016; KTM RC 390 2014–2021 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HFA6303` | air-filter | AFTERMARKET | VERIFIED | HIGH | KTM 125 Duke 2017–2023; KTM 250 Duke 2017–2023; KTM 390 Duke 2017–2023; KTM 390 Adventure 2020–2024; KTM RC 390 2022–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| EBC Brakes | `MD1194X` | brake-disc | AFTERMARKET | VERIFIED | HIGH | Honda CBR650R 2019–2027 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD2119X` | brake-disc | AFTERMARKET | PARTIAL | MEDIUM | Yamaha MT-07 2021–2026 front; Yamaha Tracer 7 2022–2024 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD2120` | brake-disc | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2026 rear; Yamaha Tracer 7 2021–2026 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD2127X` | brake-disc | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2020 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD8010` | brake-disc | AFTERMARKET | VERIFIED | HIGH | KTM 390 Duke 2018–2025 front; KTM RC 390 2017–2025 front; KTM 390 Adventure 2019–2021 front; KTM 125 Duke 2021–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD850` | brake-disc | AFTERMARKET | VERIFIED | HIGH | KTM 390 Duke 2013–2025 rear; KTM RC 390 2014–2025 rear; KTM 390 Adventure 2019–2021 rear; KTM 390 Adventure 2023–2024 rear; KTM 125 Duke 2011–2026 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `MD9156D` | brake-disc | AFTERMARKET | VERIFIED | HIGH | Yamaha NMAX 125 2015–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA140HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z900 2018–2026 rear; Kawasaki Versys 650 2015–2015 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA142HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z650 2017–2021 front; Kawasaki Versys 650 2015–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA174HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2026 rear; Yamaha Tracer 7 2021–2026 rear; Kawasaki Z650 2017–2021 rear; Kawasaki Versys 650 2016–2026 rear; Honda CRF1100L Africa Twin 2020–2026 rear; Honda CRF1100L Africa Twin 2020–2023 rear; Honda CRF1100L Africa Twin 2025–2026 rear; Suzuki V-Strom 650 2012–2022 rear; Suzuki V-Strom 650 2024–2026 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA181HH` | brake-pad | AFTERMARKET | PARTIAL | MEDIUM | Bajaj Pulsar NS200 2014–2016 front; Bajaj Dominar 400 2025–2025 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA196HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda NC750X 2014–2023 front; Honda NC750X 2014–2021 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA197HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Kawasaki Ninja 400 2017–2023 front; Kawasaki Ninja 400 2017–2023 rear; Kawasaki Ninja 250R 2008–2012 front; Honda CB125F 2015–2016 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA213HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | KTM 390 Duke 2013–2025 rear; KTM RC 390 2014–2025 rear; KTM 390 Adventure 2019–2021 rear; KTM 390 Adventure 2023–2024 rear; KTM 125 Duke 2011–2026 rear; Bajaj Pulsar NS200 2014–2016 rear; Bajaj Dominar 400 2025–2025 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA229HH` | brake-pad | AFTERMARKET | PARTIAL | MEDIUM | Suzuki V-Strom 650 2012–2022 front; Suzuki V-Strom 650 2024–2026 front; Kawasaki Versys 650 2011–2014 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA231HH` | brake-pad | AFTERMARKET | PARTIAL | MEDIUM | Suzuki V-Strom 650 2012–2022 front; Suzuki V-Strom 650 2024–2026 front; Kawasaki Versys 650 2011–2014 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA252HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2026 front; Yamaha Tracer 7 2021–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA347HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z900 2018–2024 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA388HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda CBR250R 2011–2013 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA496HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda CBR650R 2019–2027 rear; Honda NC750X 2014–2023 rear; Honda NC750X 2014–2021 rear; Honda CBR250R 2011–2013 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA606HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | KTM 390 Duke 2013–2025 front; KTM RC 390 2014–2025 front; KTM 390 Adventure 2019–2021 front; KTM 390 Adventure 2023–2024 front; KTM 125 Duke 2011–2026 front; Bajaj Dominar 400 2025–2025 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `FA679HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda CBR650R 2019–2027 front; Honda CRF1100L Africa Twin 2020–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `SFA228HH` | brake-pad | AFTERMARKET | PARTIAL | MEDIUM | Suzuki Burgman 400 2007–2022 front; Suzuki Burgman 400 2024–2024 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `SFA412HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Suzuki Burgman 400 2007–2022 rear; Suzuki Burgman 400 2024–2024 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `SFA603HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda PCX 125 2010–2017 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `SFA693HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Honda PCX 125 2021–2024 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `SFA694HH` | brake-pad | AFTERMARKET | VERIFIED | HIGH | Yamaha NMAX 125 2015–2026 front | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `CK2309` | clutch | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2024 | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `CK4527` | clutch | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z650 2017–2021 | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `CK5666` | clutch | AFTERMARKET | VERIFIED | HIGH | KTM 390 Duke 2015–2022 | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| EBC Brakes | `CSK037` | clutch | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2024 | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF132` | oil-filter | AFTERMARKET | PARTIAL | MEDIUM | Suzuki Burgman 400 1999–2006 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF138` | oil-filter | AFTERMARKET | VERIFIED | HIGH | Suzuki V-Strom 650 2004–2006; Suzuki V-Strom 650 2007–2012; Suzuki V-Strom 650 2010–2018; Suzuki V-Strom 650 2019–2019; Suzuki V-Strom 650 2020–2025; Suzuki V-Strom 650 2015–2025 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF155` | oil-filter | AFTERMARKET | VERIFIED | HIGH | KTM 125 Duke 2011–2026; KTM 250 Duke 2015–2026; KTM 390 Duke 2013–2026; KTM RC 390 2014–2024; KTM 390 Adventure 2020–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF204` | oil-filter | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–2019; Yamaha Tracer 7 2021–2021; Yamaha Tracer 7 2022–2024; Honda CBR650R 2019–2025; Honda NC750X 2014–2020; Honda NC750X 2021–2022; Honda NC750X 2023–2025; Honda CRF1100L Africa Twin 2020–2025; Honda Forza 250 2008–2012 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF205` | oil-filter | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2020–2024; Yamaha MT-07 2025–2025; Yamaha MT-07 2025–2025 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| Hiflofiltro | `HF303` | oil-filter | AFTERMARKET | VERIFIED | HIGH | Kawasaki Ninja 400 2018–2023; Kawasaki Ninja 400 2018–2020; Kawasaki Ninja 400 2021–2023; Kawasaki Ninja 400 2023–2024; Kawasaki Z650 2017–2024; Kawasaki Z900 2017–2024; Kawasaki Versys 650 2008–2014; Kawasaki Versys 650 2015–2020; Kawasaki Versys 650 2021–2024 | hiflofiltro-catalogue | gtin, price, stock, supplier, images |
| EBC Brakes | `H352` | other | AFTERMARKET | VERIFIED | HIGH | Honda PCX 125 2010–2024 rear; Honda CB125F 2015–2022 rear | ebc-brakes-catalogues | gtin, price, stock, supplier, images |
| NGK | `CPR7EA-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Honda CB125F 2015–…; Honda PCX 125 2010–2017 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CPR8EA-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Yamaha NMAX 125 2015–…; Bajaj Pulsar NS200 null–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR7E` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Suzuki Burgman 400 1999–2016 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR7EIA-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Suzuki Burgman 400 2018–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR8E` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Kawasaki Ninja 250R 2008–2012; Suzuki V-Strom 650 2007–2011 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR8EIA-9` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Suzuki V-Strom 650 2012–2016 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR9E` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Yamaha YZF-R25 2015–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `CR9EIA-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Kawasaki Z650 2017–…; Kawasaki Z900 2017–…; Kawasaki Versys 650 2007–2014; Kawasaki Versys 650 2015–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `DPR7EA-9` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Honda Forza 250 2005–2007 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `DPR8EA-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Yamaha XMAX 250 2008–2016 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `IFR6G-11K` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Honda NC750X 2021–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `IMR9E-9HES` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Honda CBR650R 2019–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `LKAR8A-9` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | KTM 390 Duke 2013–2014; KTM 125 Duke 2011–2014; Bajaj Pulsar NS200 null–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `LMAR8A-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Yamaha MT-07 2014–…; Yamaha Tracer 7 2021–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `LMAR8L-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Honda PCX 125 2021–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `LMAR9G` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Kawasaki Ninja 400 2018–2023 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `MR8E-9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Suzuki GSX-R125 2017–…; Suzuki V-Strom 650 2017–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `MR8K-9` | spark-plug | AFTERMARKET | PARTIAL | MEDIUM | Honda PCX 125 2018–2020 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `SILMAR8A9S` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Honda CRF1100L Africa Twin 2020–… | niterra-ngk-tr | gtin, price, stock, supplier, images |
| NGK | `SIMR8A9` | spark-plug | AFTERMARKET | VERIFIED | HIGH | Honda CBR250R 2011–2016 | niterra-ngk-tr | gtin, price, stock, supplier, images |
| JT Sprockets | `JTF1539` | sprocket | AFTERMARKET | PARTIAL | MEDIUM | Kawasaki Ninja 400 2018–2024 front; Kawasaki Ninja 250R 2008–2012 front | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTF1591` | sprocket | AFTERMARKET | PARTIAL | HIGH | Yamaha MT-07 2014–2024 front; Yamaha Tracer 7 2017–2024 front | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTF1908` | sprocket | AFTERMARKET | PARTIAL | HIGH | KTM 390 Duke 2013–2023 front; KTM 390 Duke 2024–2026 front; KTM RC 390 2014–2024 front; KTM 390 Adventure 2020–2024 front; Bajaj Dominar 400 null–… front | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTF520` | sprocket | AFTERMARKET | PARTIAL | MEDIUM | Suzuki V-Strom 650 2007–2019 front | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTF565` | sprocket | AFTERMARKET | PARTIAL | HIGH | Kawasaki Z650 2017–2025 front; Kawasaki Versys 650 2007–2024 front | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTR1792` | sprocket | AFTERMARKET | PARTIAL | MEDIUM | Suzuki V-Strom 650 2007–2019 rear | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTR1876` | sprocket | AFTERMARKET | PARTIAL | HIGH | Yamaha MT-07 2014–2024 rear; Yamaha Tracer 7 2017–2019 rear; Yamaha Tracer 7 2020–2024 rear | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTR478` | sprocket | AFTERMARKET | PARTIAL | HIGH | Kawasaki Z650 2017–2025 rear; Kawasaki Versys 650 2007–2024 rear | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTR486` | sprocket | AFTERMARKET | PARTIAL | HIGH | Kawasaki Ninja 400 2018–2024 rear; Kawasaki Ninja 250R 2008–2012 rear; Kawasaki Ninja 250R 2008–2012 rear | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
| JT Sprockets | `JTR890ZBK` | sprocket | AFTERMARKET | PARTIAL | MEDIUM | KTM 390 Duke 2013–2023 rear; KTM 390 Duke 2024–2026 rear; KTM RC 390 2014–2021 rear; KTM RC 390 2022–2024 rear; KTM 390 Adventure 2020–2024 rear; KTM 125 Duke 2014–2026 rear; Bajaj Dominar 400 null–… rear | jt-sprockets-catalogue | gtin, price, stock, supplier, images, orderable part number with tooth count |
