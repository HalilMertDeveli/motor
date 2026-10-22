#!/usr/bin/env node
// SHAD mounting kits: many official photos show the whole motorcycle with the kit fitted, so customers think the store
// sells motorcycles. This step crops such photos to the rear of the bike, where the kit is mounted. Genuine pixels only:
// nothing is added, the rest of the photo is cut away. Last step of the package build, after official-images.mjs build.
// Requires Node 20+ and ffmpeg/ffprobe on PATH.
//
//   node tools/supplier-images/crop-bike-photos.mjs --products=pkg/products.json --images=pkg/images --out=work/cropped
//        [--exceptions=tools/supplier-images/bike-photo-exceptions.json]
//
// The output has the same products.json and file names; only the cropped pictures change. bike-crops.csv lists every
// checked kit picture with its metrics and verdict, review.html shows the cropped ones before and after.

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Kit products: the photo shows the part on a bike. Tank bags ("Depo Üstü") are photographed on the tank and stay.
const KIT_WORDS = /Bağlantı|\b[34]P System\b/i;
const BIKE_MAKES = /(^|[^\p{L}])(Aprilia|Benelli|BMW|CF ?Moto|Ducati|Harley|Honda|Kawasaki|Keeway|KTM|Kymco|Peugeot|Piaggio|QJ|Royal Enfield|Suzuki|SYM|Triumph|Vespa|Voge|Yamaha|Zontes)(?![\p{L}])/iu;
const NOT_KIT = /Depo Üstü/i;
export const isKitProduct = p => /^shad$/i.test(p.brand || '') && (KIT_WORDS.test(p.name) || BIKE_MAKES.test(p.name)) && !NOT_KIT.test(p.name);

const LIGHT = 225;      // every channel at least this: background
const BLACK = 110;      // every channel below this: tyre
const EDGE = 0.01;      // the bike must not touch the image border (a cut-off scene is not a studio shot)
const MIN_WIDTH = 0.55; // bike width / image width
const MIN_ASPECT = 1.2; // bike box width / height
const REAR = 0.55;      // share of the bike box that is kept, from the rear wheel
const PADDING = 1.06;   // white margin around the crop
const SIDE = 1200;      // output canvas

const isDark = (d, i) => d[i] < LIGHT || d[i + 1] < LIGHT || d[i + 2] < LIGHT;
const isBlack = (d, i) => d[i] < BLACK && d[i + 1] < BLACK && d[i + 2] < BLACK;

// Whole-bike test on RGB pixels: white studio background, a wide object clear of the border, and two wheels (some row
// in the lower part of the object has dark runs far apart). Returns the metrics and the object box.
export function detectWholeBike({ w, h, data }) {
    const at = (x, y) => (y * w + x) * 3;
    const whiteBg = [[2, 2], [w - 3, 2], [2, h - 3], [w - 3, h - 3]].every(([x, y]) => !isDark(data, at(x, y)));
    let minX = w, minY = h, maxX = -1, maxY = -1;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (isDark(data, at(x, y))) {
        if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
    }
    if (maxX < 0) return { bike: false, reason: 'empty' };
    const bw = maxX - minX + 1, bh = maxY - minY + 1;
    const widthRatio = bw / w, aspect = bw / bh;
    const touches = minX < w * EDGE || maxX > w * (1 - EDGE) || minY < h * EDGE || maxY > h * (1 - EDGE);

    let wheels = false, maxGap = 0;
    for (let y = minY + Math.floor(bh * 0.5); y <= minY + Math.floor(bh * 0.9) && !wheels; y += Math.max(1, Math.floor(bh / 60))) {
        const runs = [];
        let start = -1;
        for (let x = minX; x <= maxX + 1; x++) {
            const d = x <= maxX && isBlack(data, at(x, y));
            if (d && start < 0) start = x;
            if (!d && start >= 0) { if (x - start > bw * 0.015) runs.push([start, x - 1]); start = -1; }
        }
        if (runs.length < 2) continue;
        let gap = 0;
        for (let i = 1; i < runs.length; i++) gap = Math.max(gap, runs[i][0] - runs[i - 1][1]);
        maxGap = Math.max(maxGap, gap / bw);
        // the outermost runs are the rear and the front wheel
        if ((runs.at(-1)[1] - runs[0][0]) / bw > 0.7 && gap > bw * 0.2) wheels = true;
    }
    const box = { x: minX, y: minY, w: bw, h: bh };
    const metrics = { widthRatio: +widthRatio.toFixed(3), aspect: +aspect.toFixed(3), whiteBg, touches, wheels, gap: +maxGap.toFixed(3) };
    const reason = !whiteBg ? 'background not white' : touches ? 'touches the border' : widthRatio <= MIN_WIDTH ? 'narrow'
        : aspect <= MIN_ASPECT ? 'not wide' : !wheels ? 'no two wheels' : 'whole bike';
    return { bike: reason === 'whole bike', reason, box, ...metrics };
}

// Rear REAR of the bike box at full height. SHAD photographs bikes facing right, so the rear is on the left; `mirror`
// is for the few bikes facing left. The crop is centred on a white square canvas with PADDING.
export function cropPlan(box, mirror = false) {
    const cw = Math.floor(box.w * REAR), ch = box.h;
    const x = mirror ? box.x + box.w - cw : box.x;
    const side = Math.floor(Math.max(cw, ch) * PADDING);
    return { crop: { x, y: box.y, w: cw, h: ch }, side, offset: { x: Math.floor((side - cw) / 2), y: Math.floor((side - ch) / 2) } };
}

function ffmpeg(argv) {
    return execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...argv], { maxBuffer: 512 * 1024 * 1024 });
}

export function readRgb(file) {
    const [w, h] = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file])
        .toString().trim().split(',').map(Number);
    return { w, h, data: ffmpeg(['-i', file, '-vf', 'format=rgb24', '-frames:v', '1', '-f', 'rawvideo', '-']) };
}

export function renderCrop(file, plan, target) {
    const { crop, side, offset } = plan;
    const filters = [
        `crop=${crop.w}:${crop.h}:${crop.x}:${crop.y}`,
        `pad=${side}:${side}:${offset.x}:${offset.y}:white`,
        `scale=${SIDE}:${SIDE}:flags=lanczos`,
        'format=yuvj444p'
    ];
    ffmpeg(['-y', '-i', file, '-vf', filters.join(','), '-q:v', '2', '-frames:v', '1', target]);
}

// Known false positives ("keep": top case only, lifestyle shots) and bikes facing left ("mirror"), by package file name.
export function loadExceptions(file) {
    const e = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { keep: new Set(Object.keys(e.keep || {})), mirror: new Set(Object.keys(e.mirror || {})) };
}

const csvCell = v => /[",\n;]/.test(String(v ?? '')) ? `"${String(v).replace(/"/g, '""')}"` : String(v ?? '');
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function main(args) {
    for (const k of ['products', 'images', 'out']) if (!args[k]) { console.error(`--${k}= is required`); process.exit(2); }
    const exceptions = loadExceptions(args.exceptions || path.join(path.dirname(fileURLToPath(import.meta.url)), 'bike-photo-exceptions.json'));
    const products = JSON.parse(fs.readFileSync(args.products, 'utf8'));
    const outImages = path.join(args.out, 'images'), reviewDir = path.join(args.out, 'review-img');
    fs.rmSync(args.out, { recursive: true, force: true });
    fs.mkdirSync(outImages, { recursive: true });
    fs.mkdirSync(reviewDir, { recursive: true });
    for (const f of fs.readdirSync(args.images)) fs.copyFileSync(path.join(args.images, f), path.join(outImages, f));
    fs.copyFileSync(args.products, path.join(args.out, 'products.json'));

    const rows = [];
    for (const p of products.filter(isKitProduct)) for (const name of p.images || []) {
        const file = path.join(args.images, name);
        if (!fs.existsSync(file)) continue;
        const d = detectWholeBike(readRgb(file));
        let verdict = d.bike ? 'cropped' : 'kept';
        if (d.bike && exceptions.keep.has(name)) verdict = 'kept (exception)';
        const mirror = exceptions.mirror.has(name);
        if (verdict === 'cropped') renderCrop(file, cropPlan(d.box, mirror), path.join(outImages, name));
        rows.push({ sku: p.sku, name: p.name, image: name, verdict, reason: d.reason, mirror, widthRatio: d.widthRatio, aspect: d.aspect, gap: d.gap });
    }

    const cols = ['sku', 'name', 'image', 'verdict', 'reason', 'mirror', 'widthRatio', 'aspect', 'gap'];
    fs.writeFileSync(path.join(args.out, 'bike-crops.csv'), '﻿' + [cols.join(';'), ...rows.map(r => cols.map(c => csvCell(r[c])).join(';'))].join('\r\n'));
    const thumb = (src, name) => { ffmpeg(['-y', '-i', src, '-vf', 'scale=220:220:force_original_aspect_ratio=decrease', '-q:v', '4', '-frames:v', '1', path.join(reviewDir, name)]); return `review-img/${name}`; };
    const shown = rows.filter(r => r.verdict !== 'kept' || r.mirror);
    const html = shown.map((r, i) => `<tr><td><b>${esc(r.sku)}</b><br>${esc(r.name)}<br>${esc(r.image)}</td><td>${esc(r.verdict)}${r.mirror ? ' (ayna)' : ''}</td>`
        + `<td><img src="${thumb(path.join(args.images, r.image), `a${i}.jpg`)}"></td><td><img src="${thumb(path.join(outImages, r.image), `b${i}.jpg`)}"></td></tr>`).join('');
    fs.writeFileSync(path.join(args.out, 'review.html'), `<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>SHAD bağlantı kiti kırpma</title>
<style>body{font:14px system-ui,sans-serif;margin:16px}table{border-collapse:collapse}td{border:1px solid #ddd;padding:6px;vertical-align:top}img{width:220px;height:220px;object-fit:contain;background:#fff}</style>
</head><body><h1>SHAD bağlantı kiti fotoğrafları: önce / sonra</h1><table>${html}</table></body></html>`);

    const t = rows.reduce((a, r) => (a[r.verdict] = (a[r.verdict] || 0) + 1, a), {});
    console.log('kit pictures checked:', rows.length, t);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    main(Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2))));
}
