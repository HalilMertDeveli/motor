#!/usr/bin/env node
// MOTOR: replaces the PDF crops of a supplier catalog package with the supplier's own product photos from its
// web shop (see README.md).
//
// Source: a WooCommerce shop's public Store API (`/wp-json/wc/store/v1/products`). Each package product is paired
// with a shop product by its model code and variant name, then the pairing is checked visually: the shop photo must
// look like the package picture (same shape and colour). Only checked pairs are used; everything else keeps the
// package picture. Photos are only trimmed, resized (never enlarged) and centred on a white square canvas.
//
// Requires ffmpeg/ffprobe on PATH and Node 20+.

import fs from 'node:fs';
import path from 'node:path';
import { ffmpeg, rawRgb, grayThumb, matchScore, productBox, colorSignature, colorDistance } from './image-lib.mjs';

const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
for (const k of ['products', 'images', 'store', 'out']) {
    if (!args[k]) {
        console.error('Usage: node fetch-web-photos.mjs --products=products.json --images=<package images dir> --store=https://shop.example --out=<output dir> [--fallback-images=<dir>] [--canvas=1200] [--max-photos=4] [--refresh=1]');
        process.exit(2);
    }
}

const CANVAS = +(args.canvas ?? 1200);   // longest side of the output canvas (source photos are never enlarged)
const FILL = 0.9;                        // share of the canvas the product fills
const MAX_PHOTOS = +(args['max-photos'] ?? 4);
const MAX_CANDIDATES = 6;
const SAME_PHOTO = 0.85;                 // NCC from which the shop photo is the package picture's source
const SAME_PHOTO_COLOR = 35;
const LOOKS_ALIKE = 0.6;                 // a different angle of the same product still correlates
const LOOKS_ALIKE_COLOR = 45;
const MIN_LEAD = 0.03;                   // a visual decision between several candidates needs this lead
const REQUEST_DELAY_MS = 300;
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';

const out = args.out;
const cache = path.join(out, 'cache');
const outImages = path.join(out, 'images');
const reviewDir = path.join(out, 'review');
for (const d of [cache, path.join(cache, 'thumbs'), path.join(cache, 'full'), outImages, reviewDir]) fs.mkdirSync(d, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function download(url, target) {
    if (fs.existsSync(target) && fs.statSync(target).size > 0) return target;
    for (let attempt = 1; ; attempt++) {
        const res = await fetch(url, { headers: { 'User-Agent': UA } });
        if (res.ok) {
            fs.writeFileSync(target, Buffer.from(await res.arrayBuffer()));
            await sleep(REQUEST_DELAY_MS);
            return target;
        }
        if (attempt >= 3 || res.status === 404) throw new Error(`${res.status} ${url}`);
        await sleep(2000 * attempt);
    }
}

// ---------------------------------------------------------------- shop catalog

async function shopCatalog() {
    const file = path.join(out, 'shop-products.json');
    if (fs.existsSync(file) && !args.refresh) return JSON.parse(fs.readFileSync(file, 'utf8'));
    const all = [];
    for (let page = 1; ; page++) {
        const url = `${args.store.replace(/\/$/, '')}/wp-json/wc/store/v1/products?per_page=100&page=${page}`;
        const res = await fetch(url, { headers: { 'User-Agent': UA, Accept: 'application/json' } });
        if (!res.ok) throw new Error(`${res.status} ${url}`);
        const items = await res.json();
        if (!items.length) break;
        all.push(...items.map(p => ({ id: p.id, name: p.name, permalink: p.permalink, images: p.images.map(i => ({ id: i.id, src: i.src, thumbnail: i.thumbnail })) })));
        console.log(`shop catalog: ${all.length} / ${res.headers.get('x-wp-total')}`);
        await sleep(REQUEST_DELAY_MS);
    }
    fs.writeFileSync(file, JSON.stringify(all, null, 1));
    return all;
}

// ---------------------------------------------------------------- text candidates

const fold = s => String(s ?? '').replace(/i̇/g, 'i').toLocaleUpperCase('tr-TR')
    .replace(/İ/g, 'I').replace(/Ş/g, 'S').replace(/Ğ/g, 'G').replace(/Ü/g, 'U').replace(/Ö/g, 'O').replace(/Ç/g, 'C');
const alnum = s => fold(s).replace(/[^A-Z0-9]/g, '');
const words = s => fold(s).split(/[^A-Z0-9-]+/).filter(Boolean).map(alnum).filter(Boolean);

// Words that describe the product type, not the variant.
const GENERIC = new Set(['KASK', 'FULL', 'FACE', 'YARIM', 'CENE', 'ACILIR', 'HELMETS', 'COCUK', 'JOKEY', 'MODEL', 'CHOPPER',
    'ELDIVEN', 'MONT', 'MEVSIM', 'YAZLIK', 'KISLIK', 'BOT', 'KISA', 'UZUN', 'CANTA', 'ARKA', 'YAN', 'KILIT', 'DISK', 'OFF', 'ROAD',
    'CROSS', 'KADIN', 'ERKEK', 'PANTOLON', 'YELEK', 'YAGMURLUK', 'TELEFON', 'TUTUCU', 'MOTOSIKLET', 'KASKI', 'KORUMALI', 'RACING', 'LADY']);

function shopTokens(name) {
    const t = words(name);
    const set = new Set(t);
    for (let i = 0; i < t.length; i++) {
        set.add(t[i] + (t[i + 1] ?? ''));
        set.add(t[i] + (t[i + 1] ?? '') + (t[i + 2] ?? ''));
    }
    return { list: t, set };
}

// Catalog codes end with a price code ("FF-871-03250"); the model is what comes before it.
const modelCode = p => String(p.catalogCode ?? '').replace(/[-\s]\d{3,5}$/, '');

function candidatesFor(p, shop) {
    const codeParts = words(modelCode(p));
    const codeNorm = codeParts.join('');
    const core = codeParts.find(x => /\d/.test(x)) ?? codeNorm;
    if (!core) return [];
    const variant = words(p.variant).filter(x => x.length > 1);
    const known = new Set([...codeParts, ...variant, ...words(p.name), ...words(p.brand), ...GENERIC]);
    const brand = alnum(p.brand).slice(0, 4);
    const result = [];
    for (const s of shop) {
        let code = 0;
        if (s.tokens.set.has(codeNorm)) code = 3;
        else if (s.tokens.set.has(core)) code = 2.5;
        else if (/^\d+$/.test(core) && s.tokens.list.some(t => /^[A-Z]{1,3}\d+$/.test(t) && t.endsWith(core) && t.length - core.length <= 3)) code = 2;
        else if (/^[A-Z]{1,2}\d+$/.test(core) && s.tokens.set.has(core.replace(/^[A-Z]+/, ''))) code = 1.5;
        if (!code) continue;
        if (variant.some(v => !s.tokens.set.has(v))) continue;
        // Words of the shop name that the package product does not have (e.g. "MAT" for a glossy variant).
        const extra = s.tokens.list.filter(t => !known.has(t) && !/^\d+$/.test(t) && t !== alnum(s.name.split(' ')[0]));
        const brandHit = brand && alnum(s.name).startsWith(brand) ? 0.5 : 0;
        result.push({ shop: s, text: code + brandHit - 0.5 * extra.length, exact: extra.length === 0, extra });
    }
    result.sort((a, b) => b.text - a.text);
    return result.slice(0, MAX_CANDIDATES);
}

// ---------------------------------------------------------------- visual check

const thumbCache = new Map();
function signature(key, file) {
    if (!thumbCache.has(key)) thumbCache.set(key, { thumb: grayThumb(file), color: colorSignature(file) });
    return thumbCache.get(key);
}

// Flattens transparency on white (shop photos are sometimes PNG) and writes a PNG.
function flatten(src, target) {
    ffmpeg(['-y', '-i', src, '-filter_complex', '[0:v]format=rgba,split[a][b];[a]drawbox=c=white:t=fill[bg];[bg][b]overlay=format=auto,format=rgb24', '-frames:v', '1', target]);
    return target;
}

async function visual(pkgSig, candidate) {
    let best = { score: -1, color: 999 };
    for (const img of candidate.shop.images.slice(0, MAX_PHOTOS)) {
        const ext = path.extname(new URL(img.thumbnail || img.src).pathname) || '.jpg';
        const raw = await download(img.thumbnail || img.src, path.join(cache, 'thumbs', `${img.id}${ext}`));
        const flat = path.join(cache, 'thumbs', `${img.id}.png`);
        if (!fs.existsSync(flat)) flatten(raw, flat);
        const sig = signature(`shop-${img.id}`, flat);
        const score = matchScore(pkgSig.thumb, sig.thumb);
        const color = colorDistance(pkgSig.color, sig.color);
        if (score > best.score) best = { score: +score.toFixed(3), color: +color.toFixed(1), image: img.id };
    }
    return best;
}

function decide(cands) {
    if (!cands.length) return { decision: 'none', reason: 'no shop product with this model and variant' };
    const same = c => c.visual.score >= SAME_PHOTO && c.visual.color <= SAME_PHOTO_COLOR;
    const alike = c => c.visual.score >= LOOKS_ALIKE && c.visual.color <= LOOKS_ALIKE_COLOR;
    const byVisual = [...cands].sort((a, b) => b.visual.score - a.visual.score);
    const [v1, v2] = byVisual;
    // The package picture was cut from the shop's own photo: decisive, even between several candidates.
    if (same(v1) && (!v2 || v1.visual.score - v2.visual.score >= MIN_LEAD || (v1.exact && !v2.exact))) {
        return { decision: 'accepted', reason: 'same photo', pick: v1 };
    }
    const exact = cands.filter(c => c.exact);
    if (exact.length === 1 && alike(exact[0])) return { decision: 'accepted', reason: 'model and variant match, looks alike', pick: exact[0] };
    if (cands.length === 1 && alike(cands[0]) && cands[0].text >= 2.5) return { decision: 'accepted', reason: 'only candidate, looks alike', pick: cands[0] };
    return { decision: 'review', reason: v1 ? `unclear (best ${v1.visual.score}, colour ${v1.visual.color})` : 'unclear', pick: v1 };
}

// ---------------------------------------------------------------- render

function render(src, target) {
    const flat = flatten(src, path.join(cache, 'render.png'));
    const img = rawRgb(flat);
    const box = productBox(img);
    const objMax = Math.max(box.w, box.h);
    const objectSize = Math.min(objMax, Math.round(CANVAS * FILL));
    const scale = objectSize / objMax;
    const ow = Math.max(1, Math.round(box.w * scale)), oh = Math.max(1, Math.round(box.h * scale));
    const canvas = Math.min(CANVAS, Math.round(Math.max(ow, oh) / FILL));
    ffmpeg(['-y', '-i', flat, '-vf', [
        `crop=${box.w}:${box.h}:${box.x}:${box.y}`,
        `scale=${ow}:${oh}:flags=lanczos`,
        `pad=${canvas}:${canvas}:(ow-iw)/2:(oh-ih)/2:white`,
        'format=yuvj444p'
    ].join(','), '-q:v', '3', '-frames:v', '1', target]);
    return canvas;
}

// ---------------------------------------------------------------- main

const products = JSON.parse(fs.readFileSync(args.products, 'utf8'));
const fallbackImages = args['fallback-images'] ?? args.images;
const shop = (await shopCatalog()).map(s => ({ ...s, tokens: shopTokens(s.name) }));
console.log(`package: ${products.length} products, shop: ${shop.length} products`);

const report = [];
for (const [i, p] of products.entries()) {
    const pkgPicture = (p.images ?? [])[0];
    const cands = candidatesFor(p, shop);
    const entry = { sku: p.sku, name: p.name, category: p.category, picture: pkgPicture };
    if (cands.length && pkgPicture && fs.existsSync(path.join(args.images, pkgPicture))) {
        const pkgSig = signature(`pkg-${pkgPicture}`, path.join(args.images, pkgPicture));
        for (const c of cands) c.visual = await visual(pkgSig, c);
        Object.assign(entry, decide(cands));
    } else {
        Object.assign(entry, cands.length ? { decision: 'review', reason: 'no package picture to compare' } : decide(cands));
    }
    entry.candidates = cands.map(c => ({ id: c.shop.id, name: c.shop.name, url: c.shop.permalink, text: c.text, exact: c.exact, visual: c.visual }));
    if (entry.pick) entry.pick = { id: entry.pick.shop.id, name: entry.pick.shop.name, url: entry.pick.shop.permalink, visual: entry.pick.visual };
    report.push(entry);
    if ((i + 1) % 50 === 0) console.log(`checked ${i + 1} / ${products.length}`);
}

// One shop product for several package products (e.g. a model shared by two price lines) is kept only where its
// photo is the package picture's source; the others go to review.
const byShop = new Map();
for (const r of report.filter(r => r.decision === 'accepted')) byShop.set(r.pick.id, [...(byShop.get(r.pick.id) ?? []), r]);
for (const group of byShop.values()) {
    if (group.length < 2) continue;
    const variants = new Set(group.map(r => (products.find(p => p.sku === r.sku).variant ?? '').toUpperCase()));
    if (variants.size === 1 && group.every(r => r.reason === 'same photo')) continue; // size or price variants of one product
    for (const r of group) {
        if (r.reason !== 'same photo') Object.assign(r, { decision: 'review', reason: `shop product also matched by ${group.filter(o => o !== r).map(o => o.sku).join(', ')}` });
    }
}

const shopById = new Map(shop.map(s => [s.id, s]));
const result = [];
let photos = 0;
for (const [i, p] of products.entries()) {
    const r = report.find(x => x.sku === p.sku);
    let images = [];
    if (r.decision === 'accepted') {
        const s = shopById.get(r.pick.id);
        // The photo that matched comes first.
        const ordered = [...s.images].sort((a, b) => (b.id === r.pick.visual.image) - (a.id === r.pick.visual.image)).slice(0, MAX_PHOTOS);
        for (const [n, img] of ordered.entries()) {
            const ext = path.extname(new URL(img.src).pathname) || '.jpg';
            const full = await download(img.src, path.join(cache, 'full', `${img.id}${ext}`));
            const name = `${p.sku.replace(/[^A-Za-z0-9._-]+/g, '-')}-${n + 1}.jpg`;
            if (!fs.existsSync(path.join(outImages, name))) render(full, path.join(outImages, name));
            images.push(name);
            photos++;
        }
        r.photos = ordered.map(img => img.src);
    } else {
        for (const name of p.images ?? []) {
            const src = path.join(fallbackImages, name);
            if (fs.existsSync(src)) fs.copyFileSync(src, path.join(outImages, name));
            images.push(name);
        }
    }
    result.push({ ...p, images });
    if ((i + 1) % 100 === 0) console.log(`rendered ${i + 1} / ${products.length} (${photos} shop photos)`);
}

fs.writeFileSync(path.join(out, 'products.json'), JSON.stringify(result, null, 2));
fs.writeFileSync(path.join(out, 'photo-report.json'), JSON.stringify(report, null, 1));
writeReview(report);

const count = d => report.filter(r => r.decision === d).length;
console.log(`accepted ${count('accepted')}, review ${count('review')}, none ${count('none')}; ${photos} shop photos`);

// ---------------------------------------------------------------- review page

function writeReview(rows) {
    const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    const thumb = (src, name) => {
        if (!src || !fs.existsSync(src)) return '';
        const target = path.join(reviewDir, name);
        if (!fs.existsSync(target)) ffmpeg(['-y', '-i', src, '-vf', 'scale=180:180:force_original_aspect_ratio=decrease', '-q:v', '4', '-frames:v', '1', target]);
        return `<img src="review/${esc(name)}" loading="lazy">`;
    };
    const order = { review: 0, accepted: 1, none: 2 };
    const body = [...rows].sort((a, b) => order[a.decision] - order[b.decision]).map(r => {
        const pkg = thumb(r.picture && path.join(args.images, r.picture), `pkg-${r.sku}.jpg`);
        const shopImg = r.pick?.visual?.image ? thumb(path.join(cache, 'thumbs', `${r.pick.visual.image}.png`), `shop-${r.pick.visual.image}.jpg`) : '';
        return `<tr class="${r.decision}"><td>${esc(r.decision)}<br><small>${esc(r.reason)}</small></td><td><b>${esc(r.sku)}</b><br>${esc(r.name)}</td>` +
            `<td>${pkg}</td><td>${shopImg}</td><td>${r.pick ? `<a href="${esc(r.pick.url)}">${esc(r.pick.name)}</a><br><small>NCC ${r.pick.visual?.score} · colour ${r.pick.visual?.color}</small>` : ''}</td></tr>`;
    }).join('\n');
    fs.writeFileSync(path.join(out, 'review.html'), `<!doctype html><meta charset="utf-8"><title>Shop photo review</title>
<style>body{font:14px system-ui;margin:16px}table{border-collapse:collapse}td{border-bottom:1px solid #ddd;padding:6px;vertical-align:top}
img{max-width:180px;max-height:180px}.review{background:#fff6e0}.none{color:#888}</style>
<h1>Shop photo review</h1><p>Left: package picture (PDF), right: shop photo. Accepted pairs are used; review and none keep the package picture.</p>
<table><tr><th>Decision</th><th>Product</th><th>Package</th><th>Shop</th><th>Shop product</th></tr>
${body}</table>`);
}
