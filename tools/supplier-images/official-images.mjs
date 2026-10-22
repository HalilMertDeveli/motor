#!/usr/bin/env node
// MOTOR: replaces supplier catalog pictures with the original photos from the official product pages
// (see README.md, "Official product photos").
//
// Steps, each a sub-command, so every stage can be checked before the next one runs:
//   index     reads the public product listings (WooCommerce Store API) of the configured sites
//   match     pairs package products with official pages by model code, variant and product type
//   download  fetches the pictures of matched pages (one request per second, resumable)
//   build     checks every pair visually against the catalog picture, renders the pictures and writes
//             the improved package folder, sources.csv/json and review.html/csv
//   check     verifies that no uncertain product of a package carries anything but its catalog pictures
//
// A pair is only used when the model code, the variant and the picture agree. Everything else keeps the
// catalog picture; uncertain pairs are listed but never used. Requires Node 20+ and ffmpeg/ffprobe on PATH.

import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const [command, ...rest] = process.argv.slice(2);
const args = Object.fromEntries(rest.map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
const USAGE = `Usage:
  node official-images.mjs index    --out=work/web
  node official-images.mjs match    --products=package/products.json --web=work/web --out=work/matches.json
  node official-images.mjs download --matches=work/matches.json --out=work/download
  node official-images.mjs resolve  --products=package/products.json --images=package/images --matches=work/matches.json
                                    --download=work/download --out=work/matches-resolved.json
  node official-images.mjs download --matches=work/matches-resolved.json --out=work/download
  node official-images.mjs build    --products=package/products.json --images=package/images --matches=work/matches.json
                                    --download=work/download --out=work/official [--fallback-images=improved/images] [--reject=SKU1,SKU2]
  node official-images.mjs check    --products=package/products.json --review=work/official/review.csv`;

// Official sites. `brands` limits a site to products of these package brands (null: every brand).
const SITES = [
    { host: 'www.motosev.com', label: 'motosev.com', owner: 'Motosev (supplier; owner of the Prosev brand)', brands: null },
    { host: 'www.shad.es', label: 'shad.es', owner: 'SHAD (manufacturer)', brands: ['SHAD'], refIsSku: true }
];
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const REQUEST_GAP_MS = 1000;
const MAX_PER_PRODUCT = 5;      // pictures per product, main picture of every colour page first
const TARGET_WIDTH = 1200;      // smallest WordPress size variant that is downloaded instead of the original
const CHECK_WIDTH = 500;        // size downloaded for the photo check of uncertain candidates
const CHECK_IMAGES = 3;         // pictures per candidate page used for the photo check
// The product page shows 1024 px; 1200 px keeps a package of about 1,700 pictures under the importer's 512 MB limit.
const MAX_CANVAS = 1200, MIN_CANVAS = 800;
const FILL = 0.88;              // share of the white square canvas the product fills (same look as the catalog pictures)
const MAX_UPSCALE = 1.5;
const WHITE = 238;              // a pixel darker than this in any channel belongs to the product

// Picture check against the catalog picture: grey-level shape (NCC on the trimmed product) and mean colour.
const CHECK = {
    // The colour always has to agree: a near-identical shape with another colour is another colour variant (the
    // supplier's JY01 red and black pages carry each other's photos, shape 0.98, colour distance 45).
    high: { minShape: 0.55, maxColor: 40, maxColorAlone: 18 },
    medium: { minShape: 0.70, maxColor: 32, maxColorAlone: 12 },
    // Pages accepted by `resolve` only because they show the same photo as the catalog picture.
    photo: { minShape: 0.88, maxColor: 15, maxColorAlone: -1 }
};
const SAME_PHOTO = CHECK.photo;
const SAME_PHOTO_LEAD = 0.08;   // the winning page must beat every other candidate page by this much shape score

const sleep = ms => new Promise(r => setTimeout(r, ms));
const readJson = f => JSON.parse(fs.readFileSync(f, 'utf8'));
const writeJson = (f, v) => fs.writeFileSync(f, JSON.stringify(v, null, 1));
function need(...keys) {
    for (const k of keys) if (!args[k]) { console.error(USAGE); process.exit(2); }
}

// ---------------------------------------------------------------- text normalisation

const decode = s => (s || '').replace(/&#(\d+);/g, (_, n) => String.fromCharCode(+n)).replace(/&amp;/g, '&');
const norm = s => decode(s).replace(/[İı]/g, 'I').toUpperCase()
    .replace(/Ş/g, 'S').replace(/Ğ/g, 'G').replace(/Ü/g, 'U').replace(/Ö/g, 'O').replace(/Ç/g, 'C')
    .normalize('NFD').replace(/[̀-ͯ]/g, '');
const tokens = s => norm(s).split(/[^A-Z0-9]+/).filter(Boolean);
const compact = s => norm(s).replace(/[^A-Z0-9]/g, '');

const COLOR = {
    SIYAH: 'BLACK', BEYAZ: 'WHITE', KIRMIZI: 'RED', MAVI: 'BLUE', SARI: 'YELLOW', GRI: 'GREY', GRAY: 'GREY',
    PEMBE: 'PINK', YESIL: 'GREEN', TURUNCU: 'ORANGE', MOR: 'PURPLE', KAHVERENGI: 'BROWN', LACIVERT: 'NAVY', MATT: 'MAT',
    FLUO: 'NEON', FLORESAN: 'NEON', FLUORESCENT: 'NEON', SEFFAF: 'CLEAR', FUME: 'SMOKE', KAMUFLAJ: 'CAMO', GUMUS: 'SILVER',
    TITANYUM: 'TITANIUM', ALUMINYUM: 'ALU', KARBON: 'CARBON',
    // Spanish (shad.es)
    NEGRO: 'BLACK', BLANCO: 'WHITE', ROJO: 'RED', AZUL: 'BLUE', GRIS: 'GREY', AMARILLO: 'YELLOW', VERDE: 'GREEN',
    PLATA: 'SILVER', TITANIO: 'TITANIUM', CARBONO: 'CARBON', ALUMINIO: 'ALU'
};
const canon = t => COLOR[t] || t;
const COLOR_WORDS = 'BLACK WHITE RED BLUE YELLOW GREY PINK GREEN ORANGE PURPLE BROWN NAVY MAT NEON CLEAR SMOKE CAMO SILVER GOLD ROSE CARBON BEIGE KHAKI TITANIUM BRONZE'.split(' ');

// Product type and descriptive words: they never distinguish two variants.
const GENERIC = new Set(`KASK FULL FACE YARIM CENE ACILIR ACIK KAPALI CROSS MONT MEVSIM 4 ELDIVEN BOT KISA UZUN YAZLIK KISLIK COCUK
    JOKEY CHOPPER HELMETS HELMET LOCK KILIT KILIDI ARKA CANTA CANTASI YAN TOP CASE ORTA PANTOLON YELEK KORUMA KORUMALI
    DIZLIK DIRSEKLIK MOTOSIKLET KADIN ERKEK WOMEN WOMAN LADY MEN INTERCOM INTERKOM SET SETI TAKIMI TAKIM YAGMURLUK GOZLUK
    CAMLI CAMI CAM KEVLAR DERI BOYUNLUK BANDANA BAF MASKE SPREY BOYA TELEFON TUTUCU AYNA BRANDA REFLEKTORLU THE AND VE
    RACING TURING TOURING BOTU AYAKKABI JACKET GLOVES GLOVE HORGUCLU YENI NEW EVO MODEL`.split(/\s+/).filter(Boolean));

const BRAND_ALIASES = {
    'Scoyco': ['SCOYCO'], 'MTS': ['MTS'], 'SHAD': ['SHAD'], 'Prosev': ['PROSEV'], 'Free-M': ['FREEM', 'FREE'],
    'Fazer': ['FAZER'], 'GAFF': ['GAFF'], 'Nikko': ['NIKKO'], 'Kinguard': ['KINGUARD'], 'Pro Helmets': ['PRO', 'PROHELMETS'],
    'YOHE': ['YOHE'], 'RSV': ['RSV'], 'MXS': ['MXS'], 'Nolann': ['NOLANN'], 'Milenco': ['MILENCO'], 'JDS': ['JDS'],
    'Jiekai': ['JIEKAI'], 'Zoli': ['ZOLI'], 'SHIRO': ['SHIRO'], 'MXPRO': ['MXPRO'], 'Raleri': ['RALERI'], 'Blinker': ['BLINKER'],
    'YEMA': ['YEMA']
};
const ALL_BRAND_TOKENS = new Set(Object.values(BRAND_ALIASES).flat().concat(['HELMETS']));
const AMBIGUOUS_BRAND_TOKENS = ['HELMETS', 'PRO', 'FREE'];

const PART_WORDS = /\b(CAMI|CAM|VIZOR|APARAT|APARATI|SWITCH|SPOILER|PED|PEDI|PINLOCK|TEREK|TEREGI|VIDA|ANTIFOG|YEDEK)\b/;
const HELMET_WORDS = /\b(KASK|HELMET)\b/;
// shad.es names are Spanish; without an exact SHAD reference only a page whose type fits the category is accepted.
const SHAD_TYPE = [[/Top Case/i, /TOP CASE|BAUL/], [/Yan Çanta/i, /MALETA|ALFORJA|LATERAL/], [/Depo/i, /DEPOSITO|TANK/]];

// Model code: the catalog code without its price group, which always starts with 0 ("869-01780" -> "869").
const modelCode = p => norm(p.catalogCode || p.sku).trim().replace(/[- ]0\d*$/, '');
const isCodeLike = c => c.length >= 3 && /\d/.test(c);

// ---------------------------------------------------------------- index

async function index() {
    need('out');
    fs.mkdirSync(args.out, { recursive: true });
    for (const site of SITES) {
        const all = [];
        for (let page = 1; ; page++) {
            const url = `https://${site.host}/wp-json/wc/store/v1/products?per_page=100&page=${page}`;
            const res = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'application/json' } });
            if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
            const items = await res.json();
            all.push(...items);
            const pages = +res.headers.get('x-wp-totalpages') || page;
            console.log(`${site.label}: page ${page}/${pages}, ${all.length} products`);
            if (page >= pages || !items.length) break;
            await sleep(REQUEST_GAP_MS * 2);
        }
        writeJson(path.join(args.out, `${site.label}.json`), { site: site.label, fetchedAt: new Date().toISOString(), products: all });
    }
}

// ---------------------------------------------------------------- match

function loadWeb(dir) {
    const items = [];
    for (const site of SITES) {
        const file = path.join(dir, `${site.label}.json`);
        if (!fs.existsSync(file)) continue;
        for (const w of readJson(file).products) {
            const toks = (site.refIsSku ? [compact(site.label.split('.')[0])] : []).concat(tokens(w.name));
            const item = {
                site, id: w.id, name: decode(w.name), url: w.permalink, ref: site.refIsSku ? compact(w.sku || '') : null, toks,
                images: (w.images || []).map(i => ({ id: i.id, thumb: i.thumbnail, ...pickSize(i) }))
            };
            // All windows of 1 to 3 consecutive tokens, compacted, with the index of their last token.
            item.windows = new Map();
            for (let i = 0; i < toks.length; i++) {
                let acc = '';
                for (let j = i; j < Math.min(i + 3, toks.length); j++) {
                    acc += toks[j];
                    if (!item.windows.has(acc)) item.windows.set(acc, j);
                }
            }
            items.push(item);
        }
    }
    return items;
}

// The smallest WordPress size of at least TARGET_WIDTH, else the original upload.
function pickSize(img) {
    const set = (img.srcset || '').split(',').map(s => s.trim().split(/\s+/)).filter(a => a.length === 2 && /w$/.test(a[1]))
        .map(([u, w]) => ({ u, w: parseInt(w) }));
    const largest = set.reduce((m, x) => x.w > (m?.w || 0) ? x : m, null);
    const fit = set.filter(x => x.w >= TARGET_WIDTH).sort((a, b) => a.w - b.w)[0];
    // A small uncropped size for the photo check (WooCommerce's square thumbnails are cropped).
    const dims = x => x.u.match(/-(\d+)x(\d+)\.\w+$/)?.slice(1).map(Number);
    const [lw, lh] = (largest && dims(largest)) || [];
    const check = set.filter(x => x.w >= CHECK_WIDTH && (!lw || !dims(x) || Math.abs(dims(x)[0] / dims(x)[1] - lw / lh) < 0.03))
        .sort((a, b) => a.w - b.w)[0];
    return { url: fit ? fit.u : img.src, width: fit ? fit.w : largest?.w || null, original: img.src, check: check?.u || (fit ? fit.u : img.src) };
}

function match() {
    need('products', 'web', 'out');
    const products = readJson(args.products);
    const web = loadWeb(args.web);

    // Words that distinguish variants: colours, package variant words and letter words of package codes ("BIG").
    const vocab = new Set(COLOR_WORDS);
    for (const p of products) {
        for (const t of tokens(p.variant || '')) if (!GENERIC.has(canon(t))) vocab.add(canon(t));
        for (const c of p.colors || []) for (const t of tokens(c)) vocab.add(canon(t));
        for (const t of tokens(modelCode(p))) if (!/\d/.test(t) && t.length > 1) vocab.add(canon(t));
    }
    for (const b of ALL_BRAND_TOKENS) vocab.delete(b);
    const packageCodes = new Set(products.map(p => compact(modelCode(p))));

    function candidates(p, cc, brandToks) {
        const out = [];
        for (const w of web) {
            if (w.site.brands && !w.site.brands.includes(p.brand)) continue;
            let kind = null, end = -1;
            if (w.ref && w.ref === cc) kind = 'ref';
            else if (w.windows.has(cc)) { kind = 'exact'; end = w.windows.get(cc); }
            else if (/^\d{3,}$/.test(cc)) {
                // A letter prefix on the web side only for numeric codes, e.g. "M-910" for "910".
                for (const [win, j] of w.windows) {
                    if (win.endsWith(cc) && /^[A-Z]{1,2}$/.test(win.slice(0, -cc.length))) { kind = 'prefixed'; end = j; break; }
                }
            }
            if (!kind) continue;
            // "910" must not silently take the page of "M-910" when the package has both lines.
            if (kind === 'exact' && end > 0 && w.toks[end] === cc && /^[A-Z]{1,2}$/.test(w.toks[end - 1]) && packageCodes.has(w.toks[end - 1] + cc)) kind = 'prefixed';
            // A helmet page must not stand in for a visor, pad or other spare part, and vice versa.
            const pkgHelmet = /^Kask >/.test(p.category || '');
            const webPart = PART_WORDS.test(norm(w.name));
            const webHelmet = HELMET_WORDS.test(norm(w.name)) && !webPart;
            if (!w.site.refIsSku && (pkgHelmet ? webPart : webHelmet)) continue;
            if (w.site.refIsSku && kind !== 'ref') {
                const rule = SHAD_TYPE.find(([cat]) => cat.test(p.category || ''));
                if (!rule || !rule[1].test(norm(w.name))) continue;
            }
            const hasBrand = !!w.site.refIsSku || brandToks.some(b => w.toks.includes(b));
            if (!hasBrand && w.toks.some(t => ALL_BRAND_TOKENS.has(t) && !AMBIGUOUS_BRAND_TOKENS.includes(t))) continue;
            const webVar = (end >= 0 ? w.toks.slice(end + 1) : w.toks).map(canon)
                .filter(t => !GENERIC.has(t) && !brandToks.includes(t) && !ALL_BRAND_TOKENS.has(t) && !/^\d+$/.test(t));
            out.push({ w, kind, hasBrand, webVar });
        }
        return out;
    }

    const results = [];
    for (const p of products) {
        const brandToks = BRAND_ALIASES[p.brand] || [];
        const code = modelCode(p);
        const nameToks = new Set(tokens(p.name).map(canon));
        const colorToks = (p.colors || []).flatMap(c => tokens(c).map(canon));
        const r = {
            sku: p.sku, brand: p.brand || '', name: p.name, category: p.category, code, variant: p.variant || '',
            oldImages: p.images || [], status: 'not-found', confidence: null, reason: '', candidates: []
        };
        results.push(r);

        // The full code first, then trailing letter words move into the variant ("JK152 GREY BIG SIZE" -> "JK152").
        let codeToks = tokens(code), peeled = [], cands = [], cc = '';
        while (codeToks.length) {
            cc = codeToks.join('');
            if (!isCodeLike(cc)) break;
            cands = candidates(p, cc, brandToks);
            if (cands.length || codeToks.length === 1 || /\d/.test(codeToks.at(-1))) break;
            peeled.unshift(canon(codeToks.pop()));
        }
        // Letter prefix on the package side: "FR-965" is listed as "965" on the site. Needs the brand on the page and
        // counts as a weaker match (stricter picture check).
        if (!peeled.length && /^[A-Z]{1,3}$/.test(tokens(code)[0] || '') && /^\d{3,}$/.test(tokens(code)[1] || '')) {
            const rest = tokens(code).slice(1).join('');
            const known = new Set(cands.map(c => c.w));
            const extra = candidates(p, rest, brandToks).filter(c => c.hasBrand && !known.has(c.w)).map(c => ({ ...c, kind: 'prefixed' }));
            if (extra.length && !cands.length) cc = rest;
            cands = cands.concat(extra);
        }
        // Letter-only codes ("MKM", "DO SPACY") are too generic for a name match: such pages only count when the brand
        // is named and `resolve` finds the very same photo as the catalog picture.
        if (!isCodeLike(cc)) {
            cands = cc.length >= 2 ? candidates(p, cc, brandToks).filter(c => c.kind === 'exact' && c.hasBrand).map(c => ({ ...c, kind: 'letters' })) : [];
            if (!cands.length) { r.reason = `model code "${code}" is too generic to match safely`; continue; }
        }
        if (!cands.length) { r.reason = `no official product page with model code ${code}`; continue; }

        const want = new Set([...tokens(p.variant || '').map(canon), ...peeled].filter(t => !GENERIC.has(t) && !brandToks.includes(t)));
        for (const c of cands) {
            const have = new Set(c.webVar);
            c.missing = [...want].filter(t => !have.has(t));
            c.extra = [...have].filter(t => !want.has(t) && !nameToks.has(t));
            // An exact manufacturer reference identifies the article; its descriptive words cannot conflict.
            c.conflict = c.kind === 'ref' ? [] : c.extra.filter(t => vocab.has(t));
            const webHasVariant = [...have].some(t => vocab.has(t) && !nameToks.has(t));
            c.fit = c.missing.length === 0 && c.conflict.length === 0 ? 'exact'
                : c.missing.length === 0 ? 'extra-variant'
                : !webHasVariant ? 'unspecified' : 'mismatch';
        }
        const describe = c => ({
            site: c.w.site.label, owner: c.w.site.owner, id: c.w.id, name: c.w.name, url: c.w.url, images: c.w.images,
            kind: c.kind, fit: c.fit, extra: c.extra, conflict: c.conflict, missing: c.missing, hasBrand: c.hasBrand
        });
        const strong = c => (c.kind === 'exact' || c.kind === 'ref') && (c.hasBrand || cc.length >= 4);

        let exact = cands.filter(c => c.fit === 'exact');
        // The same article on the supplier's and on the manufacturer's site (one page each): the manufacturer wins.
        const sites = new Set(exact.map(c => c.w.site.label));
        if (exact.length > 1 && sites.size === exact.length) {
            const maker = exact.filter(c => c.w.site.refIsSku);
            if (maker.length === 1) exact = maker;
        }
        const plausible = cands.filter(c => c.fit !== 'mismatch');
        if (exact.length === 1 && exact[0].kind === 'letters') {
            r.status = 'uncertain'; r.reason = `letter-only model code "${code}": needs the same photo`; r.candidates = [describe(exact[0])];
        } else if (exact.length === 1) {
            const c = exact[0];
            r.status = 'matched';
            r.confidence = strong(c) && c.hasBrand ? 'high' : 'medium';
            r.reason = `${c.kind} model code, ${want.size ? 'variant' : 'no variant'} matches` + (c.hasBrand ? '' : ', brand not named on the page');
            r.candidates = [describe(c)];
        } else if (exact.length === 0 && want.size === 0 && colorToks.length > 1) {
            // One package product with several colours, the site has one page per colour (e.g. MC48 Kevlar black/brown).
            const colourPages = cands.filter(c => strong(c) && c.conflict.length && c.conflict.every(t => colorToks.includes(t)));
            const ordered = colorToks.map(t => colourPages.find(c => c.conflict.includes(t))).filter((c, i, a) => c && a.indexOf(c) === i);
            if (ordered.length >= 2 && ordered.length === colourPages.length) {
                r.status = 'matched'; r.confidence = 'medium';
                r.reason = 'colour pages of one product: ' + ordered.map(c => c.conflict.join(' ')).join(', ');
                r.candidates = ordered.map(describe);
            } else {
                r.status = 'uncertain'; r.reason = 'model found, colours do not line up'; r.candidates = cands.slice(0, 6).map(describe);
            }
        } else if (exact.length > 1) {
            r.status = 'uncertain'; r.reason = 'several equally good pages'; r.candidates = exact.slice(0, 6).map(describe);
        } else if (plausible.length === 1) {
            const c = plausible[0];
            r.status = 'uncertain'; r.confidence = 'low';
            r.reason = c.fit === 'unspecified' ? `page does not name the variant "${[...want].join(' ')}"` : `page names another variant word (${c.conflict.join(' ')})`;
            r.candidates = [describe(c)];
        } else if (plausible.length > 1) {
            r.status = 'uncertain'; r.reason = 'several pages, variant unclear'; r.candidates = plausible.slice(0, 6).map(describe);
        } else {
            r.reason = `model ${code} is on the site, but not the variant "${[...want].join(' ')}"`;
            r.candidates = cands.slice(0, 6).map(describe);
        }
        // Uncertain pairs can still be settled by `resolve` when one page shows the very same photo.
        r.photoCheck = r.status === 'uncertain' && r.oldImages.length > 0 && r.candidates.length > 0;
    }

    writeJson(args.out, results);
    const key = r => r.status + (r.confidence ? '/' + r.confidence : '');
    console.log(results.reduce((a, r) => (a[key(r)] = (a[key(r)] || 0) + 1, a), {}));
}

// ---------------------------------------------------------------- download

// Pictures of a matched product in download order: the main picture of every colour page first.
function plannedImages(m) {
    const order = [];
    const longest = Math.max(0, ...m.candidates.map(c => c.images.length));
    for (let i = 0; i < longest; i++) {
        for (const c of m.candidates) if (c.images[i]) order.push({ ...c.images[i], page: c });
    }
    return order.slice(0, MAX_PER_PRODUCT);
}

const fileFor = (url, kind) => path.join(kind, crypto.createHash('sha1').update(url).digest('hex').slice(0, 16)
    + ((path.extname(new URL(url).pathname) || '.jpg').toLowerCase().replace('.jpeg', '.jpg')));

async function download() {
    need('matches', 'out');
    const matches = readJson(args.matches);
    fs.mkdirSync(args.out, { recursive: true });
    const logPath = path.join(args.out, 'download-log.json');
    const log = fs.existsSync(logPath) ? readJson(logPath) : {};

    // Full pictures for matched products; small uncropped pictures of uncertain candidates for `resolve` and the review.
    const jobs = new Map();
    for (const m of matches) {
        if (m.status === 'matched') for (const img of plannedImages(m)) jobs.set(img.url, 'full');
        else if (m.photoCheck) for (const c of m.candidates) for (const img of c.images.slice(0, CHECK_IMAGES)) if (!jobs.has(img.check)) jobs.set(img.check, 'check');
    }
    console.log(`${jobs.size} pictures, ${[...jobs.keys()].filter(u => log[u]?.ok).length} already downloaded`);

    let done = 0, bytes = 0, failed = 0;
    for (const [url, kind] of jobs) {
        if (log[url]?.ok) continue;
        const file = fileFor(url, kind);
        fs.mkdirSync(path.join(args.out, kind), { recursive: true });
        let entry;
        try {
            const res = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'image/avif,image/webp,image/*,*/*;q=0.8' } });
            if (res.status === 429 || res.status === 503) { console.log(`rate limited (HTTP ${res.status}), stopping; run again later to resume`); break; }
            const type = res.headers.get('content-type') || '';
            if (!res.ok || !type.startsWith('image/')) throw new Error(`HTTP ${res.status} ${type}`);
            const buf = Buffer.from(await res.arrayBuffer());
            if (buf.length > 20 * 1024 * 1024) throw new Error('larger than 20 MB');
            fs.writeFileSync(path.join(args.out, file), buf);
            entry = { ok: true, file, kind, bytes: buf.length, contentType: type, downloadedAt: new Date().toISOString() };
            bytes += buf.length;
        } catch (e) {
            entry = { ok: false, error: String(e.message || e), kind, downloadedAt: new Date().toISOString() };
            failed++;
        }
        log[url] = entry;
        if (++done % 25 === 0) { writeJson(logPath, log); console.log(`${done} downloaded, ${Math.round(bytes / 1048576)} MB, ${failed} failed`); }
        await sleep(REQUEST_GAP_MS);
    }
    writeJson(logPath, log);
    console.log(`finished: ${done} new, ${Math.round(bytes / 1048576)} MB, ${failed} failed`);
}

// ---------------------------------------------------------------- picture helpers

function ffmpeg(argv) {
    return execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...argv], { maxBuffer: 512 * 1024 * 1024 });
}

function size(file) {
    const [w, h] = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file])
        .toString().trim().split(',').map(Number);
    return { w, h };
}

// RGB pixels at a bounded working size (big originals are shrunk first; trimming does not need full resolution).
function rawRgb(file, maxSide = 1600) {
    const s = size(file);
    const k = Math.min(1, maxSide / Math.max(s.w, s.h));
    const w = Math.max(1, Math.round(s.w * k)), h = Math.max(1, Math.round(s.h * k));
    const data = ffmpeg(['-i', file, '-vf', `scale=${w}:${h}:flags=area,format=rgb24`, '-frames:v', '1', '-f', 'rawvideo', '-']);
    return { w, h, data, scale: k, src: s };
}

const isDark = (d, i) => d[i] < WHITE || d[i + 1] < WHITE || d[i + 2] < WHITE;

// Bounding box of the product on a white background; null when the border is not white (photo with a scene).
function productBox({ w, h, data }) {
    let border = 0, dark = 0;
    for (let x = 0; x < w; x++) for (const y of [0, h - 1]) { border++; if (isDark(data, (y * w + x) * 3)) dark++; }
    for (let y = 0; y < h; y++) for (const x of [0, w - 1]) { border++; if (isDark(data, (y * w + x) * 3)) dark++; }
    if (dark / border > 0.08) return null;
    const rowHas = y => { let c = 0; for (let x = 0; x < w; x++) if (isDark(data, (y * w + x) * 3) && ++c > 1) return true; return false; };
    const colHas = x => { let c = 0; for (let y = 0; y < h; y++) if (isDark(data, (y * w + x) * 3) && ++c > 1) return true; return false; };
    let top = 0, bottom = h - 1, left = 0, right = w - 1;
    while (top < bottom && !rowHas(top)) top++;
    while (bottom > top && !rowHas(bottom)) bottom--;
    while (left < right && !colHas(left)) left++;
    while (right > left && !colHas(right)) right--;
    return { x: left, y: top, w: right - left + 1, h: bottom - top + 1 };
}

// Signature for the picture check: grey thumbnail and the mean colour of the product pixels.
function signature(file) {
    const data = ffmpeg(['-i', file, '-vf', 'scale=48:-1:flags=area,format=rgb24', '-frames:v', '1', '-f', 'rawvideo', '-']);
    let r = 0, g = 0, b = 0, n = 0;
    for (let i = 0; i + 2 < data.length; i += 3) if (isDark(data, i)) { r += data[i]; g += data[i + 1]; b += data[i + 2]; n++; }
    return { thumb: grayThumb(file), color: n ? [r / n, g / n, b / n] : [255, 255, 255] };
}

// Grey thumbnail for the crop-aware shape match (same method as enhance-supplier-images.mjs).
const MATCH_WIDTH = 96;
function grayThumb(file) {
    const data = ffmpeg(['-i', file, '-vf', `scale=${MATCH_WIDTH}:-1:flags=area,format=gray`, '-frames:v', '1', '-f', 'rawvideo', '-']);
    return { w: MATCH_WIDTH, h: Math.floor(data.length / MATCH_WIDTH), data: Float64Array.from(data) };
}

function resample(img, w, h) {
    const out = new Float64Array(w * h);
    for (let y = 0; y < h; y++) {
        const sy = Math.min(img.h - 1, (y + 0.5) * img.h / h - 0.5);
        const y0 = Math.max(0, Math.floor(sy)), y1 = Math.min(img.h - 1, y0 + 1), fy = Math.max(0, sy - y0);
        for (let x = 0; x < w; x++) {
            const sx = Math.min(img.w - 1, (x + 0.5) * img.w / w - 0.5);
            const x0 = Math.max(0, Math.floor(sx)), x1 = Math.min(img.w - 1, x0 + 1), fx = Math.max(0, sx - x0);
            const a = img.data[y0 * img.w + x0], b = img.data[y0 * img.w + x1], c = img.data[y1 * img.w + x0], d = img.data[y1 * img.w + x1];
            out[y * w + x] = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }
    }
    return { w, h, data: out };
}

// Best NCC of `tpl` placed anywhere inside `img`, sampling every `step` pixels.
function bestPlacement(img, tpl, step) {
    const n = tpl.w * tpl.h;
    let tMean = 0;
    for (const v of tpl.data) tMean += v;
    tMean /= n;
    let tVar = 0;
    for (const v of tpl.data) tVar += (v - tMean) ** 2;
    if (tVar < 1e-6) return -1;
    let best = -1;
    for (let oy = 0; oy + tpl.h <= img.h; oy += step) {
        for (let ox = 0; ox + tpl.w <= img.w; ox += step) {
            let s = 0, s2 = 0, st = 0;
            for (let y = 0; y < tpl.h; y++) {
                const row = (oy + y) * img.w + ox, trow = y * tpl.w;
                for (let x = 0; x < tpl.w; x++) { const v = img.data[row + x]; s += v; s2 += v * v; st += v * (tpl.data[trow + x] - tMean); }
            }
            const iVar = s2 - s * s / n;
            if (iVar < 1e-6) continue;
            best = Math.max(best, st / Math.sqrt(iVar * tVar));
        }
    }
    return best;
}

function placementAt(img, tpl, scale, step) {
    const tw = Math.round(img.w * scale), th = Math.round(tw * tpl.h / tpl.w);
    if (tw < 16 || th < 16 || tw > img.w || th > img.h) return -1;
    return bestPlacement(img, resample(tpl, tw, th), step);
}

// Either picture may be a crop of the other (catalog crops are cut and carry labels): coarse scale search, then fine.
function shapeScore(a, b) {
    let best = { score: -1 };
    for (let s = 1.0; s >= 0.35; s -= 0.05) {
        for (const pair of [[b, a], [a, b]]) {
            const score = placementAt(pair[0], pair[1], s, 2);
            if (score > best.score) best = { score, s, pair };
        }
    }
    if (best.score < 0.5) return best.score;
    let fine = best.score;
    for (let s = best.s - 0.05; s <= best.s + 0.05; s += 0.01) fine = Math.max(fine, placementAt(best.pair[0], best.pair[1], s, 1));
    return fine;
}

// Agreement between the catalog picture and an official picture: crop-aware shape NCC and mean product colour.
function compare(oldSig, newSig) {
    const shape = shapeScore(oldSig.thumb, newSig.thumb);
    const color = Math.hypot(...oldSig.color.map((v, i) => v - newSig.color[i]));
    return { shape: +shape.toFixed(3), color: +color.toFixed(1) };
}

const passes = (c, t) => (c.shape >= t.minShape && c.color <= t.maxColor) || (c.color <= t.maxColorAlone && c.shape >= t.minShape - 0.15);

// White square canvas like the catalog pictures: trimmed product filling FILL, never enlarged more than MAX_UPSCALE.
function render(file, target) {
    const img = rawRgb(file);
    const box = productBox(img);
    const k = 1 / img.scale; // working size -> source pixels
    const crop = box ? { x: Math.floor(box.x * k), y: Math.floor(box.y * k), w: Math.ceil(box.w * k), h: Math.ceil(box.h * k) } : { x: 0, y: 0, ...img.src };
    crop.w = Math.min(crop.w, img.src.w - crop.x); crop.h = Math.min(crop.h, img.src.h - crop.y);
    const objMax = Math.max(crop.w, crop.h);
    const fill = box ? FILL : 1; // a photo with a scene keeps its full frame
    const canvas = Math.round(Math.min(MAX_CANVAS, Math.max(MIN_CANVAS, Math.min(objMax * MAX_UPSCALE, MAX_CANVAS * fill) / fill)));
    const scale = Math.min(MAX_UPSCALE, canvas * fill / objMax);
    const ow = Math.max(1, Math.round(crop.w * scale)), oh = Math.max(1, Math.round(crop.h * scale));
    const filters = [
        `crop=${crop.w}:${crop.h}:${crop.x}:${crop.y}`,
        `scale=${ow}:${oh}:flags=lanczos`,
        `pad=${canvas}:${canvas}:(ow-iw)/2:(oh-ih)/2:white`,
        'format=yuvj444p'
    ];
    ffmpeg(['-y', '-i', file, '-vf', filters.join(','), '-q:v', '2', '-frames:v', '1', target]);
    return { canvas, sourceWidth: img.src.w, sourceHeight: img.src.h, trimmed: !!box };
}

// ---------------------------------------------------------------- resolve

// Settles uncertain products by the photo itself: a candidate page counts only when one of its pictures is the very
// same photo as the catalog picture (shape and colour), and clearly better than every other candidate page.
function resolve() {
    need('products', 'images', 'matches', 'download', 'out');
    const products = new Map(readJson(args.products).map(p => [p.sku, p]));
    const matches = readJson(args.matches);
    const log = readJson(path.join(args.download, 'download-log.json'));
    let promoted = 0, checked = 0;
    for (const m of matches) {
        if (!m.photoCheck || m.status !== 'uncertain') continue;
        const oldFile = path.join(args.images, products.get(m.sku).images[0]);
        if (!fs.existsSync(oldFile)) continue;
        checked++;
        const old = signature(oldFile);
        const scored = m.candidates.map(c => {
            const results = c.images.slice(0, CHECK_IMAGES).map(img => log[img.check]).filter(e => e?.ok)
                .map(e => compare(old, signature(path.join(args.download, e.file))));
            const best = results.reduce((a, x) => (x.shape - x.color / 100) > (a.shape - a.color / 100) ? x : a, { shape: -1, color: 999 });
            return { c, best };
        }).sort((a, b) => b.best.shape - a.best.shape);
        m.photoScores = scored.map(s => ({ id: s.c.id, name: s.c.name, ...s.best }));
        const [win, next] = scored;
        if (win && passes(win.best, SAME_PHOTO) && (!next || win.best.shape - next.best.shape >= SAME_PHOTO_LEAD || !passes(next.best, CHECK.high))) {
            m.status = 'matched';
            m.confidence = 'photo';
            m.reason = `same photo as the catalog picture (shape ${win.best.shape}, colour distance ${win.best.color}); before: ${m.reason}`;
            m.candidates = [win.c];
            promoted++;
        }
    }
    writeJson(args.out, matches);
    console.log(`photo check: ${checked} uncertain products checked, ${promoted} promoted to matched`);
}

// ---------------------------------------------------------------- build

const csvCell = v => /[",\n;]/.test(String(v ?? '')) ? `"${String(v).replace(/"/g, '""')}"` : String(v ?? '');
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function build() {
    need('products', 'images', 'matches', 'download', 'out');
    const products = readJson(args.products);
    const matches = new Map(readJson(args.matches).map(m => [m.sku, m]));
    // SKUs a person rejected on the contact sheets keep their catalog picture.
    const rejected = new Set((args.reject || '').split(',').map(s => s.trim()).filter(Boolean));
    const log = readJson(path.join(args.download, 'download-log.json'));
    const outImages = path.join(args.out, 'images');
    const reviewDir = path.join(args.out, 'review-img');
    fs.rmSync(args.out, { recursive: true, force: true });
    fs.mkdirSync(outImages, { recursive: true });
    fs.mkdirSync(reviewDir, { recursive: true });

    // Pictures that are not replaced are copied unchanged: from --fallback-images when given (e.g. the improved
    // package with the same file names), else from the package. The check always compares with the package picture.
    const keepDir = args['fallback-images'] || args.images;
    for (const f of fs.readdirSync(args.images)) {
        const keep = fs.existsSync(path.join(keepDir, f)) ? path.join(keepDir, f) : path.join(args.images, f);
        fs.copyFileSync(keep, path.join(outImages, f));
    }

    const sigCache = new Map();
    const sig = f => { if (!sigCache.has(f)) sigCache.set(f, signature(f)); return sigCache.get(f); };
    const sources = [], review = [];
    const slug = s => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

    for (const p of products) {
        const m = matches.get(p.sku);
        const row = {
            sku: p.sku, brand: p.brand || '', name: p.name, status: m?.status || 'not-found', reason: m?.reason || '',
            oldImages: p.images || [], newImages: [], pages: (m?.candidates || []).map(c => ({ site: c.site, name: c.name, url: c.url })), check: null, thumbs: []
        };
        review.push(row);
        if (!m) continue;

        if (m.status === 'uncertain') {
            for (const c of m.candidates) for (const img of c.images.slice(0, 2)) {
                const e = log[img.check] || log[img.thumb];
                if (e?.ok) row.thumbs.push({ file: path.join(args.download, e.file), page: c.url });
            }
            continue;
        }
        if (m.status !== 'matched') continue;
        if (rejected.has(p.sku)) { row.status = 'uncertain'; row.reason = `rejected in manual review; ${m.reason}`; continue; }

        let planned = plannedImages(m).map(img => ({ img, entry: log[img.url] })).filter(x => x.entry?.ok);
        if (!planned.length) { row.status = 'not-found'; row.reason = 'pictures of the official page could not be downloaded'; continue; }
        const files = planned.map(x => path.join(args.download, x.entry.file));

        // Picture check: the catalog picture must agree with at least one official picture.
        const oldFile = p.images?.length ? path.join(args.images, p.images[0]) : null;
        if (oldFile && fs.existsSync(oldFile)) {
            const checks = files.map(f => compare(sig(oldFile), sig(f)));
            const bestIndex = checks.reduce((bi, c, i) => (c.shape - c.color / 100) > (checks[bi].shape - checks[bi].color / 100) ? i : bi, 0);
            const best = checks[bestIndex];
            row.check = best;
            // The site's first picture stays the main picture unless it fails the check (some pages show another
            // colour first); then the official picture closest to the catalog picture moves to the front.
            if (bestIndex > 0 && !passes(checks[0], CHECK[m.confidence] || CHECK.medium)) {
                planned = [planned[bestIndex], ...planned.filter((_, i) => i !== bestIndex)];
            }
            if (!passes(best, CHECK[m.confidence] || CHECK.medium)) {
                row.status = 'uncertain';
                row.reason = `picture differs from the catalog picture (shape ${best.shape}, colour distance ${best.color}); ${m.reason}`;
                row.thumbs = files.slice(0, 2).map(f => ({ file: f, page: planned[0].img.page.url }));
                continue;
            }
        } else if (m.confidence !== 'high') {
            row.status = 'uncertain'; row.reason = `no catalog picture to compare with; ${m.reason}`;
            row.thumbs = files.slice(0, 2).map(f => ({ file: f, page: planned[0].img.page.url }));
            continue;
        }

        const base = slug(p.sku);
        const names = [];
        planned.forEach((x, i) => {
            const name = `${base}-o${i + 1}.jpg`;
            const info = render(path.join(args.download, x.entry.file), path.join(outImages, name));
            names.push(name);
            sources.push({
                sku: p.sku, brand: p.brand || '', image: name, position: i + 1, site: x.img.page.site, owner: x.img.page.owner,
                page: x.img.page.url, pageName: x.img.page.name, sourceUrl: x.img.url, originalUrl: x.img.original,
                downloadedAt: x.entry.downloadedAt, sourceSize: `${info.sourceWidth}x${info.sourceHeight}`, canvas: info.canvas,
                shapeScore: i === 0 ? row.check?.shape ?? '' : '', colorDistance: i === 0 ? row.check?.color ?? '' : ''
            });
        });
        row.newImages = names;
        p.images = names;
    }

    // Catalog pictures that are no longer referenced are not shipped.
    const used = new Set(products.flatMap(p => p.images || []));
    for (const f of fs.readdirSync(outImages)) if (!used.has(f)) fs.rmSync(path.join(outImages, f));

    writeJson(path.join(args.out, 'products.json'), products);
    writeJson(path.join(args.out, 'sources.json'), sources);
    const srcCols = Object.keys(sources[0] || { sku: '' });
    fs.writeFileSync(path.join(args.out, 'sources.csv'), '﻿' + [srcCols.join(';'), ...sources.map(s => srcCols.map(c => csvCell(s[c])).join(';'))].join('\r\n'));
    writeReview(review, args.out, args.images, reviewDir, outImages);
}

const STATUS_TR = { matched: 'Eşleşti', uncertain: 'Belirsiz', 'not-found': 'Bulunamadı' };

function writeReview(rows, outDir, oldImagesDir, reviewDir, newImagesDir) {
    const thumb = (src, name) => {
        const target = path.join(reviewDir, name);
        try { ffmpeg(['-y', '-i', src, '-vf', 'scale=180:180:force_original_aspect_ratio=decrease', '-q:v', '4', '-frames:v', '1', target]); return `review-img/${name}`; } catch { return null; }
    };
    const cols = ['brand', 'sku', 'name', 'status', 'reason', 'oldImages', 'newImages', 'pages', 'shape', 'color'];
    const csv = rows.map(r => [r.brand, r.sku, r.name, STATUS_TR[r.status], r.reason, r.oldImages.join(' '), r.newImages.join(' '),
        r.pages.map(x => x.url).join(' '), r.check?.shape ?? '', r.check?.color ?? ''].map(csvCell).join(';'));
    fs.writeFileSync(path.join(outDir, 'review.csv'), '﻿' + [cols.join(';'), ...csv].join('\r\n'));

    const byBrand = {};
    for (const r of rows) { const b = r.brand || '(markasız)'; byBrand[b] ??= { matched: 0, uncertain: 0, 'not-found': 0 }; byBrand[b][r.status]++; }
    const summary = Object.entries(byBrand).sort((a, b) => (b[1].matched + b[1].uncertain + b[1]['not-found']) - (a[1].matched + a[1].uncertain + a[1]['not-found']))
        .map(([b, v]) => `<tr><td>${esc(b)}</td><td>${v.matched}</td><td>${v.uncertain}</td><td>${v['not-found']}</td></tr>`).join('');

    let i = 0;
    const section = (status, withPictures) => rows.filter(r => r.status === status).map(r => {
        const pics = [];
        if (withPictures) {
            const old = r.oldImages[0] && fs.existsSync(path.join(oldImagesDir, r.oldImages[0])) ? thumb(path.join(oldImagesDir, r.oldImages[0]), `o${++i}.jpg`) : null;
            if (old) pics.push(`<figure><img src="${old}" loading="lazy"><figcaption>katalog</figcaption></figure>`);
            for (const n of r.newImages.slice(0, 2)) { const t = thumb(path.join(newImagesDir, n), `n${++i}.jpg`); if (t) pics.push(`<figure><img src="${t}" loading="lazy"><figcaption>resmi</figcaption></figure>`); }
            for (const t of r.thumbs.slice(0, 3)) { const x = thumb(t.file, `c${++i}.jpg`); if (x) pics.push(`<figure><img src="${x}" loading="lazy"><figcaption>aday</figcaption></figure>`); }
        }
        const pages = r.pages.map(x => `<a href="${esc(x.url)}">${esc(x.name)}</a>`).join('<br>');
        const check = r.check ? ` · şekil ${r.check.shape}, renk farkı ${r.check.color}` : '';
        return `<tr><td>${esc(r.brand)}</td><td><b>${esc(r.sku)}</b><br>${esc(r.name)}</td><td>${esc(r.reason)}${check}<br>${pages}</td><td class="pics">${pics.join('')}</td></tr>`;
    }).join('');

    const html = `<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Resmi ürün fotoğrafları — inceleme</title>
<style>body{font:14px system-ui,sans-serif;margin:16px;color:#111}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:6px;vertical-align:top;text-align:left}
.pics{white-space:nowrap}figure{display:inline-block;margin:0 6px 0 0;text-align:center}img{width:120px;height:120px;object-fit:contain;background:#fff;border:1px solid #eee}
figcaption{font-size:11px;color:#666}h2{margin-top:32px}</style></head><body>
<h1>Resmi ürün fotoğrafları — inceleme listesi</h1>
<p>Eşleşti: model kodu, varyant ve fotoğraf kontrolü tuttu; resmi fotoğraf kullanıldı. Belirsiz: katalog fotoğrafı kaldı; aday fotoğraflar kullanılmaz (sahip kararı 2026-10-22). Bulunamadı: resmi sayfa yok; katalog fotoğrafı kaldı.</p>
<table><tr><th>Marka</th><th>Eşleşti</th><th>Belirsiz</th><th>Bulunamadı</th></tr>${summary}</table>
<h2>Belirsiz</h2><table>${section('uncertain', true)}</table>
<h2>Eşleşti</h2><table>${section('matched', true)}</table>
<h2>Bulunamadı</h2><table>${section('not-found', false)}</table></body></html>`;
    fs.writeFileSync(path.join(outDir, 'review.html'), html);

    const t = rows.reduce((a, r) => (a[r.status] = (a[r.status] || 0) + 1, a), {});
    console.log('products:', t, 'pictures replaced or added:', rows.reduce((a, r) => a + r.newImages.length, 0));
}

// ---------------------------------------------------------------- check

// Owner decision 2026-10-22: photos of "uncertain" products are never used, not even after a manual or another
// tool's review; these products keep their catalog pictures. `check` verifies a package before it is imported
// (e.g. one merged from other sources) against the review.csv of the official run and exits with 1 on a violation.
function parseCsv(text) {
    const rows = [];
    let row = [], cell = '', quoted = false;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quoted) {
            if (c === '"' && text[i + 1] === '"') { cell += '"'; i++; } else if (c === '"') quoted = false; else cell += c;
        } else if (c === '"') quoted = true;
        else if (c === ';') { row.push(cell); cell = ''; }
        else if (c === '\n') { row.push(cell.replace(/\r$/, '')); rows.push(row); row = []; cell = ''; }
        else cell += c;
    }
    if (cell || row.length) { row.push(cell); rows.push(row); }
    const [head, ...body] = rows;
    return body.filter(r => r.length === head.length).map(r => Object.fromEntries(head.map((k, i) => [k, r[i]])));
}

function check() {
    need('products', 'review');
    const products = readJson(args.products);
    const review = parseCsv(fs.readFileSync(args.review, 'utf8').replace(/^﻿/, ''));
    const uncertain = new Map(review.filter(r => r.status === STATUS_TR.uncertain).map(r => [r.sku, new Set(r.oldImages.split(' ').filter(Boolean))]));
    const violations = products.filter(p => uncertain.has(p.sku) && (p.images || []).some(f => !uncertain.get(p.sku).has(f)));
    for (const p of violations) console.log(`${p.sku}: ${(p.images || []).filter(f => !uncertain.get(p.sku).has(f)).join(' ')}`);
    console.log(`uncertain products: ${uncertain.size}, in package: ${products.filter(p => uncertain.has(p.sku)).length}, with other than catalog pictures: ${violations.length}`);
    if (violations.length) process.exit(1);
}

// ---------------------------------------------------------------- main

const commands = { index, match, download, resolve, build, check };
if (!commands[command]) { console.error(USAGE); process.exit(2); }
await commands[command]();
