#!/usr/bin/env node
// MOTOR: builds a reference catalog of SHAD products (reference code, page, gallery photos) from shad.es.
//
// Reads the public product sitemaps and the English product pages (robots.txt allows both; no query strings).
// The output is used by fetch-web-photos.mjs (--references=...) to pair SHAD products by their exact reference.
//
// Usage: node crawl-shad-references.mjs --out=work/shad-references.json [--products=package/products.json] [--delay=1000] [--concurrency=2]

import fs from 'node:fs';

const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
if (!args.out) {
    console.error('Usage: node crawl-shad-references.mjs --out=shad-references.json [--products=products.json] [--delay=1000] [--concurrency=2]');
    process.exit(2);
}

const BASE = 'https://www.shad.es';
const DELAY = +(args.delay ?? 1000);
const CONCURRENCY = +(args.concurrency ?? 2); // the site is slow per page, not rate limited
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function get(url) {
    for (let attempt = 1; ; attempt++) {
        const res = await fetch(url, { headers: { 'User-Agent': UA } });
        if (res.ok) return res.text();
        if (attempt >= 3 || res.status === 404) throw new Error(`${res.status} ${url}`);
        await sleep(3000 * attempt);
    }
}

const decode = s => s.replace(/&amp;/g, '&').replace(/&#8211;/g, '-').replace(/&#8217;/g, "'").replace(/&quot;/g, '"').trim();

function parse(url, html) {
    // The reference is shown as a heading ("H0NG77IF") and in the fitting instructions' file name.
    const refs = new Set();
    for (const m of html.matchAll(/elementor-heading-title[^>]*>\s*([A-Z0-9]{5,14})\s*</g)) {
        if (/\d/.test(m[1]) && /[A-Z]/.test(m[1])) refs.add(m[1]);
    }
    for (const m of html.matchAll(/ANEXOS_WEB\/([A-Z0-9]{5,14})\.pdf/g)) refs.add(m[1]);
    const images = [...new Set([...html.matchAll(/woocommerce-product-gallery__image[^>]*>\s*<a href="([^"]+)"/g)].map(m => m[1]))];
    const title = decode((html.match(/<h1[^>]*>([\s\S]*?)<\/h1>/) ?? [])[1]?.replace(/<[^>]+>/g, '') ?? '');
    return { url, title, refs: [...refs], images };
}

const index = await get(`${BASE}/sitemap_index.xml`);
const sitemaps = [...index.matchAll(/<loc>([^<]*product-sitemap\d*\.xml)<\/loc>/g)].map(m => m[1]);
const urls = [];
for (const sitemap of sitemaps) {
    const xml = await get(sitemap);
    urls.push(...[...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => m[1]).filter(u => u.startsWith(`${BASE}/en/`)));
    await sleep(DELAY);
}
let pages = [...new Set(urls)];
console.log(`${pages.length} English product pages`);

// shad.es answers slowly (seconds per page): with --products, only pages whose address shares at least two words
// (vehicle model, case model) with a SHAD product of the package are read.
if (args.products) {
    const fold = s => s.toLowerCase().replace(/ı/g, 'i').normalize('NFD').replace(/\p{M}/gu, '');
    const tokens = s => fold(s).split(/[^a-z0-9]+/).filter(t => t.length > 1);
    const generic = new Set(['shad', 'system', 'bag', 'baglanti', 'demiri', 'yan', 'canta', 'arka', 'depo', 'ustu', 'click', 'sistemi', 've', 'icin', 'kit', 'set']);
    const slugs = pages.map(url => ({ url, words: new Set(tokens(url.split('/').slice(-2, -1)[0])) }));
    const wanted = new Set();
    for (const p of JSON.parse(fs.readFileSync(args.products, 'utf8')).filter(p => /^shad$/i.test(p.brand ?? ''))) {
        const words = tokens(p.name).filter(t => !generic.has(t));
        const scored = slugs.map(s => ({ url: s.url, n: words.filter(w => s.words.has(w)).length })).filter(s => s.n >= 2).sort((a, b) => b.n - a.n);
        scored.filter(s => s.n === scored[0]?.n).slice(0, 4).forEach(s => wanted.add(s.url));
    }
    pages = pages.filter(u => wanted.has(u));
    console.log(`${pages.length} pages match package products`);
}

const done = fs.existsSync(args.out) ? JSON.parse(fs.readFileSync(args.out, 'utf8')) : [];
const seen = new Set(done.map(x => x.url));
const queue = pages.filter(u => !seen.has(u));
let count = 0;
async function worker() {
    for (let url = queue.shift(); url; url = queue.shift()) {
        try {
            done.push(parse(url, await get(url)));
        } catch (e) {
            console.log(`skip ${url}: ${e.message}`);
        }
        if (++count % 25 === 0) {
            fs.writeFileSync(args.out, JSON.stringify(done, null, 1));
            console.log(`${count} / ${pages.length - seen.size}`);
        }
        await sleep(DELAY);
    }
}
await Promise.all(Array.from({ length: CONCURRENCY }, worker));
fs.writeFileSync(args.out, JSON.stringify(done, null, 1));
console.log(`saved ${done.length} pages, ${done.filter(x => x.refs.length).length} with a reference`);
