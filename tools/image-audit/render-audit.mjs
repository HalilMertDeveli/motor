#!/usr/bin/env node
// Rendered image QA: opens storefront pages in headless Chromium at desktop, tablet and mobile size, loads every lazy
// picture and checks each visible <img> for broken files, unwanted crops, stretching, clipping and soft enlargement
// (rules in render-checks.mjs). Also opens the product photo viewer and checks it starts at fit-to-screen.
//
//   node tools/image-audit/render-audit.mjs --base=http://127.0.0.1:5107 --out=reports/image-audit/render
//        [--pages=/,/kask/] [--products=3] [--screenshots=1] [--chrome=path/to/chrome.exe]
//
// Exit code 1 when a defect is found, so it can gate a deploy. Writes render-defects.csv and render-summary.json.

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright-core';
import { classify, measureImages } from './render-checks.mjs';

// Pictures a human already judged (fixes.json "reviewed") are reported but do not fail the run
const REVIEWED = new Map(JSON.parse(fs.readFileSync(new URL('fixes.json', import.meta.url), 'utf8')).reviewed.flatMap(r => r.mediaIds.map(id => [id, r.verdict])));
const reviewedVerdict = src => REVIEWED.get(+(/\/media\/(\d+)\//.exec(src || '')?.[1]));

const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
const BASE = (args.base || 'http://127.0.0.1:5107').replace(/\/$/, '');
const OUT = args.out || 'reports/image-audit/render';
const PRODUCTS_PER_CATEGORY = +(args.products ?? 2);
const SCREENSHOTS = args.screenshots !== '0';

// Real browser user agents: the store answers "HeadlessChrome" with 403 (bot protection)
const DESKTOP_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36';
const MOBILE_UA = 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36';

export const VIEWPORTS = [
    { name: 'desktop', width: 1440, height: 900, deviceScaleFactor: 1, userAgent: DESKTOP_UA },
    { name: 'tablet', width: 768, height: 1024, deviceScaleFactor: 2, isMobile: true, hasTouch: true, userAgent: MOBILE_UA },
    { name: 'mobile', width: 390, height: 844, deviceScaleFactor: 2, isMobile: true, hasTouch: true, userAgent: MOBILE_UA }
];

// Playwright's own browser download, or a Chrome/Edge install.
function findChrome() {
    if (args.chrome) return args.chrome;
    const roots = [path.join(os.homedir(), 'AppData/Local/ms-playwright'), path.join(os.homedir(), '.cache/ms-playwright')];
    for (const root of roots) {
        if (!fs.existsSync(root)) continue;
        for (const dir of fs.readdirSync(root).filter(d => /^chromium-\d+$/.test(d)).sort().reverse()) {
            for (const exe of ['chrome-win64/chrome.exe', 'chrome-linux64/chrome', 'chrome-linux/chrome']) {
                const p = path.join(root, dir, exe);
                if (fs.existsSync(p)) return p;
            }
        }
    }
    return undefined; // playwright-core then fails with a clear message
}

// Hide the cookie dialog without consenting to anything.
const HIDE_CONSENT = '#cookie-manager-window, .modal-backdrop { display:none !important } body.modal-open { overflow:auto !important }';

async function settle(page) {
    await page.addStyleTag({ content: HIDE_CONSENT }).catch(() => {});
    // Scroll in steps so lazy pictures load, then back to the top
    await page.evaluate(async () => {
        const step = innerHeight * 0.8;
        for (let y = 0; y < document.body.scrollHeight; y += step) { scrollTo(0, y); await new Promise(r => setTimeout(r, 120)); }
        scrollTo(0, 0);
    });
    await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
    await page.evaluate(() => Promise.all([...document.images].filter(i => !i.complete).map(i => new Promise(r => { i.onload = i.onerror = r; setTimeout(r, 5000); }))));
}

async function auditPage(page, url, vp, results) {
    const failed = [];
    const onResponse = r => { if (r.request().resourceType() === 'image' && r.status() >= 400) failed.push(`${r.status()} ${r.url()}`); };
    const onConsole = m => { if (m.type() === 'error') failed.push(`console: ${m.text().slice(0, 200)}`); };
    page.on('response', onResponse);
    page.on('console', onConsole);
    const res = await page.goto(BASE + url, { waitUntil: 'domcontentloaded', timeout: 60000 });
    await settle(page);
    const measured = await page.evaluate(measureImages);
    page.off('response', onResponse);
    page.off('console', onConsole);

    const defects = [];
    for (const m of measured) for (const d of classify(m)) defects.push({ viewport: vp.name, url, src: m.src.replace(BASE, ''), context: m.context, ...d });
    for (const f of failed) defects.push({ viewport: vp.name, url, src: f, context: '', type: f.startsWith('console') ? 'console-error' : 'http-error', detail: f });
    results.push({ viewport: vp.name, url, status: res?.status(), images: measured.length, defects: defects.length });

    if (SCREENSHOTS) {
        const file = path.join(OUT, 'screens', `${vp.name}${url.replace(/[^a-z0-9]+/gi, '_')}.png`);
        await page.screenshot({ path: file, fullPage: false });
    }
    return defects;
}

// Product photo viewer: opens at fit-to-screen, zooms only when asked, and loads the full-size file.
async function auditGallery(page, url, vp) {
    await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
    await settle(page);
    // The "İncele" button opens the viewer (a DOM click: on touch layouts it only shows after a tap)
    const opened = await page.evaluate(() => { const b = document.querySelector('#pd-gallery .motor-gal-expand'); if (b) b.click(); return !!b; });
    if (!opened) return [{ viewport: vp.name, url, type: 'gallery-missing', detail: 'no product picture to open' }];
    const viewer = page.locator('.pswp--open');
    if (!await viewer.waitFor({ timeout: 5000 }).then(() => true, () => false)) return [{ viewport: vp.name, url, type: 'gallery-not-opening', detail: 'photo viewer did not open' }];
    await page.waitForTimeout(600);
    const state = await page.evaluate(() => {
        const img = document.querySelector('.pswp__item:not([aria-hidden="true"]) .pswp__img:not(.pswp__img--placeholder)') || document.querySelector('.pswp__img');
        const r = img.getBoundingClientRect();
        return { w: r.width, h: r.height, vw: innerWidth, vh: innerHeight, naturalWidth: img.naturalWidth, src: img.currentSrc, zoomed: !!document.querySelector('.pswp--zoomed-in') };
    });
    const defects = [];
    // Fit-to-screen: the whole picture is inside the viewport at open
    if (state.zoomed || state.w > state.vw + 2 || state.h > state.vh + 2) defects.push({ viewport: vp.name, url, src: state.src, type: 'gallery-overzoom', detail: `opens at ${Math.round(state.w)}x${Math.round(state.h)} in ${state.vw}x${state.vh}` });
    if (SCREENSHOTS) await page.screenshot({ path: path.join(OUT, 'screens', `${vp.name}_gallery${url.replace(/[^a-z0-9]+/gi, '_')}.png`) });
    await page.keyboard.press('Escape');
    return defects;
}

async function discover(page) {
    await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' });
    const cats = await page.evaluate(() => [...new Set([...document.querySelectorAll('.navbar a[href^="/"], .motor-cat-link[href^="/"]')].map(a => a.getAttribute('href')))]
        .filter(h => h.length > 1 && !/^\/(login|cart|wishlist|compare|contactus|newproducts|recently|manufacturer|search)/.test(h)));
    const products = [];
    for (const c of cats) {
        await page.goto(BASE + c, { waitUntil: 'domcontentloaded' });
        const links = await page.evaluate(() => [...new Set([...document.querySelectorAll('.artlist .art-name a, .artlist a.art-name-link')].map(a => a.getAttribute('href')))]);
        products.push(...links.slice(0, PRODUCTS_PER_CATEGORY));
    }
    return { cats, products: [...new Set(products)] };
}

async function main() {
    fs.mkdirSync(path.join(OUT, 'screens'), { recursive: true });
    const browser = await chromium.launch({ executablePath: findChrome() });
    const all = [], results = [];
    try {
        let pages;
        if (args.pages) pages = { cats: args.pages.split(','), products: (args['product-pages'] || '').split(',').filter(Boolean) };
        else {
            const ctx = await browser.newContext({ userAgent: DESKTOP_UA });
            pages = await discover(await ctx.newPage());
            await ctx.close();
        }
        const extra = args.pages ? [] : ['/', '/search/?q=kask', '/manufacturer/all/'];
        for (const vp of VIEWPORTS) {
            const ctx = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, deviceScaleFactor: vp.deviceScaleFactor, isMobile: vp.isMobile, hasTouch: vp.hasTouch, userAgent: vp.userAgent });
            const page = await ctx.newPage();
            for (const url of [...extra, ...pages.cats, ...pages.products]) {
                try { all.push(...await auditPage(page, url, vp, results)); }
                catch (e) { all.push({ viewport: vp.name, url, type: 'page-error', detail: e.message.split('\n')[0] }); }
            }
            for (const url of pages.products.slice(0, +(args.galleries ?? 6))) {
                try { all.push(...await auditGallery(page, url, vp)); }
                catch (e) { all.push({ viewport: vp.name, url, type: 'gallery-error', detail: e.message.split('\n')[0] }); }
            }
            await ctx.close();
        }
    } finally {
        await browser.close();
    }

    for (const d of all) d.verdict = reviewedVerdict(d.src) || '';
    const open = all.filter(d => !d.verdict);
    const cols = ['viewport', 'url', 'type', 'verdict', 'src', 'context', 'detail'];
    const csv = [cols.join(','), ...all.map(d => cols.map(c => JSON.stringify(String(d[c] ?? ''))).join(','))].join('\n');
    fs.writeFileSync(path.join(OUT, 'render-defects.csv'), csv + '\n');
    const byType = all.reduce((a, d) => (a[d.type] = (a[d.type] || 0) + 1, a), {});
    fs.writeFileSync(path.join(OUT, 'render-summary.json'), JSON.stringify({ base: BASE, pages: results.length, images: results.reduce((a, r) => a + r.images, 0), defects: all.length, open: open.length, byType, results }, null, 1));
    console.log(`pages ${results.length}, images ${results.reduce((a, r) => a + r.images, 0)}, defects ${all.length}, open ${open.length}`, byType);
    process.exitCode = open.length ? 1 : 0;
}

if (process.argv[1]?.endsWith("render-audit.mjs")) {
    main().catch(e => { console.error(e); process.exitCode = 2; });
}
