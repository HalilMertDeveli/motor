#!/usr/bin/env node
// MOTOR: improves product pictures of a supplier catalog package (see README.md).
//
// Only genuine pixels are used, nothing is generated: each package picture is matched to the original image
// embedded in the supplier's PDF (native resolution, complete, not cut by a page crop), cleaned (alpha mask on
// white, JPEG deblocking), trimmed, resized with Lanczos (at most MAX_UPSCALE) and centred on a white square canvas.
// Pictures without a reliable match fall back to the package picture with the same cleanup.
//
// Requires ffmpeg on PATH and the output of `pdfimages -all -p` plus `pdfimages -list` (poppler).

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
for (const k of ['products', 'images', 'pdf-images', 'pdf-list', 'out']) {
    if (!args[k]) {
        console.error(`Usage: node enhance-supplier-images.mjs --products=products.json --images=<package images dir> --pdf-images=<pdfimages -all -p output dir> --pdf-list=<pdfimages -list output> --out=<output dir> [--only=name1,name2]`);
        process.exit(2);
    }
}

const MATCH_WIDTH = 96;        // width of the grayscale thumbnails used for matching
const MIN_SCORE = 0.86;        // normalized cross-correlation needed to trust a match on the product's page
const MIN_SCORE_OTHER_PAGE = 0.93;
const MAX_COLOR_DISTANCE = 40; // mean product colour (RGB) must agree, so colour variants are not mixed up
const MIN_CANDIDATE_SIDE = 120; // smaller embedded images are icons, logos, flags
const MAX_UPSCALE = 2.5;       // never enlarge genuine pixels more than this
const MAX_OBJECT = 880;        // longest side of the product on the canvas
const FILL = 0.88;             // share of the canvas the product fills
const MIN_CANVAS = 600, MAX_CANVAS = 1000;
const WHITE = 238;             // a pixel darker than this in any channel belongs to the product

const outImages = path.join(args.out, 'images');
const tmp = path.join(args.out, '.tmp');
fs.mkdirSync(outImages, { recursive: true });
fs.mkdirSync(tmp, { recursive: true });

// ---------------------------------------------------------------- pdf image list (pairs images with soft masks)

const rows = fs.readFileSync(args['pdf-list'], 'utf8').split(/\r?\n/).slice(2)
    .map(l => l.trim().split(/\s+/)).filter(c => c.length > 10)
    .map(c => ({ page: +c[0], num: +c[1], type: c[2], w: +c[3], h: +c[4], enc: c[8] }));

const pdfFiles = new Map(fs.readdirSync(args['pdf-images']).map(f => {
    const m = f.match(/^[^-]+-(\d+)-(\d+)\.(\w+)$/);
    return m ? [+m[2], path.join(args['pdf-images'], f)] : null;
}).filter(Boolean));

const candidatesByPage = new Map();
rows.forEach((r, i) => {
    if (r.type !== 'image' || Math.min(r.w, r.h) < MIN_CANDIDATE_SIDE || !pdfFiles.has(r.num)) return;
    const next = rows[i + 1];
    const mask = next && next.type === 'smask' && next.page === r.page && next.w === r.w && next.h === r.h ? pdfFiles.get(next.num) : null;
    const list = candidatesByPage.get(r.page) ?? [];
    list.push({ ...r, file: pdfFiles.get(r.num), mask });
    candidatesByPage.set(r.page, list);
});

// ---------------------------------------------------------------- ffmpeg helpers

function ffmpeg(argv, opts = {}) {
    return execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...argv], { maxBuffer: 256 * 1024 * 1024, ...opts });
}

// Composites an image (and its soft mask) on white and writes a lossless PNG at native size.
function flatten(src, mask, w, h, target) {
    if (mask) {
        ffmpeg(['-y', '-i', src, '-i', mask, '-filter_complex',
            `color=c=white:s=${w}x${h}[bg];[0:v]format=rgba[c];[1:v]format=gray[m];[c][m]alphamerge[fg];[bg][fg]overlay=format=auto,format=rgb24`,
            '-frames:v', '1', target]);
    } else {
        ffmpeg(['-y', '-i', src, '-vf', 'format=rgb24', '-frames:v', '1', target]);
    }
    return target;
}

function rawRgb(file) {
    const probe = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file]).toString().trim().split(',');
    const w = +probe[0], h = +probe[1];
    return { w, h, data: ffmpeg(['-i', file, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-']) };
}

function grayThumb(file) {
    const data = ffmpeg(['-i', file, '-vf', `scale=${MATCH_WIDTH}:-1:flags=area,format=gray`, '-f', 'rawvideo', '-']);
    return { w: MATCH_WIDTH, h: Math.floor(data.length / MATCH_WIDTH), data: Float64Array.from(data) };
}

// ---------------------------------------------------------------- matching (multi-scale normalized cross-correlation)

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

// Best NCC of `tpl` placed anywhere inside `img` (tpl must fit).
function bestPlacement(img, tpl) {
    const n = tpl.w * tpl.h;
    let tMean = 0;
    for (const v of tpl.data) tMean += v;
    tMean /= n;
    let tVar = 0;
    for (const v of tpl.data) tVar += (v - tMean) ** 2;
    if (tVar < 1e-6) return -1;

    let best = -1;
    for (let oy = 0; oy + tpl.h <= img.h; oy += 2) {
        for (let ox = 0; ox + tpl.w <= img.w; ox += 2) {
            let s = 0, s2 = 0, st = 0;
            for (let y = 0; y < tpl.h; y++) {
                const row = (oy + y) * img.w + ox, trow = y * tpl.w;
                for (let x = 0; x < tpl.w; x++) {
                    const v = img.data[row + x];
                    s += v; s2 += v * v; st += v * (tpl.data[trow + x] - tMean);
                }
            }
            const iVar = s2 - s * s / n;
            if (iVar < 1e-6) continue;
            const score = st / Math.sqrt(iVar * tVar);
            if (score > best) best = score;
        }
    }
    return best;
}

function matchScore(a, b) {
    // a is the package picture, b the embedded original; either may be a crop of the other.
    let best = -1;
    for (let s = 1.0; s >= 0.35; s -= 0.05) {
        for (const [img, tpl] of [[b, a], [a, b]]) {
            const tw = Math.round(img.w * s), th = Math.round(tw * tpl.h / tpl.w);
            if (tw < 16 || th < 16 || th > img.h) continue;
            best = Math.max(best, bestPlacement(img, resample(tpl, tw, th)));
        }
    }
    return best;
}

// ---------------------------------------------------------------- cleanup and canvas

function productBox({ w, h, data }) {
    const dark = (x, y) => {
        const i = (y * w + x) * 3;
        return data[i] < WHITE || data[i + 1] < WHITE || data[i + 2] < WHITE;
    };
    const rowHas = y => { let c = 0; for (let x = 0; x < w; x++) if (dark(x, y) && ++c > 1) return true; return false; };
    const colHas = x => { let c = 0; for (let y = 0; y < h; y++) if (dark(x, y) && ++c > 1) return true; return false; };
    let top = 0, bottom = h - 1, left = 0, right = w - 1;
    while (top < bottom && !rowHas(top)) top++;
    while (bottom > top && !rowHas(bottom)) bottom--;
    while (left < right && !colHas(left)) left++;
    while (right > left && !colHas(right)) right--;
    return { x: left, y: top, w: right - left + 1, h: bottom - top + 1 };
}

function render(flatFile, deblock, maxUpscale, target) {
    const img = rawRgb(flatFile);
    const box = productBox(img);
    const objMax = Math.max(box.w, box.h);
    const scale = Math.min(MAX_OBJECT / objMax, maxUpscale);
    const ow = Math.max(1, Math.round(box.w * scale)), oh = Math.max(1, Math.round(box.h * scale));
    const canvas = Math.min(MAX_CANVAS, Math.max(MIN_CANVAS, Math.round(Math.max(ow, oh) / FILL)));
    const filters = [
        `crop=${box.w}:${box.h}:${box.x}:${box.y}`,
        ...(deblock ? ['nlmeans=s=1.2:p=3:r=7'] : []),
        `scale=${ow}:${oh}:flags=lanczos`,
        ...(scale > 1.05 ? ['unsharp=5:5:0.55:5:5:0'] : []),
        `pad=${canvas}:${canvas}:(ow-iw)/2:(oh-ih)/2:white`,
        'format=yuvj444p'
    ];
    ffmpeg(['-y', '-i', flatFile, '-vf', filters.join(','), '-q:v', '2', '-frames:v', '1', target]);
    return { box, scale: +scale.toFixed(2), canvas };
}

// ---------------------------------------------------------------- main

const products = JSON.parse(fs.readFileSync(args.products, 'utf8'));
const only = args.only ? new Set(args.only.split(',')) : null;
const refs = [];
for (const p of products) {
    (p.images ?? []).forEach(name => refs.push({ name, page: p.pdfPage, sku: p.sku }));
}

const thumbCache = new Map();
const thumbOf = (key, make) => {
    if (!thumbCache.has(key)) thumbCache.set(key, grayThumb(make()));
    return thumbCache.get(key);
};

// Mean colour of the product pixels (non-white), on a small thumbnail.
function colorSignature(file) {
    const data = ffmpeg(['-i', file, '-vf', 'scale=48:-1:flags=area,format=rgb24', '-f', 'rawvideo', '-']);
    let r = 0, g = 0, b = 0, n = 0;
    for (let i = 0; i + 2 < data.length; i += 3) {
        if (data[i] < WHITE || data[i + 1] < WHITE || data[i + 2] < WHITE) {
            r += data[i]; g += data[i + 1]; b += data[i + 2]; n++;
        }
    }
    return n ? [r / n, g / n, b / n] : [255, 255, 255];
}

const colorCache = new Map();
const colorOf = (key, file) => {
    if (!colorCache.has(key)) colorCache.set(key, colorSignature(file));
    return colorCache.get(key);
};

const distance = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);

const report = [];
let done = 0;
for (const ref of refs) {
    if (only && !only.has(ref.name)) continue;
    const pkgFile = path.join(args.images, ref.name);
    if (!fs.existsSync(pkgFile)) {
        report.push({ ...ref, status: 'missing' });
        continue;
    }

    const pkgThumb = grayThumb(pkgFile);
    const pkgColor = colorSignature(pkgFile);
    let best = null;
    for (const page of [ref.page, ref.page - 1, ref.page + 1]) {
        // The product's own page is the normal case; a neighbouring page must match almost perfectly.
        const required = page === ref.page ? MIN_SCORE : MIN_SCORE_OTHER_PAGE;
        for (const c of candidatesByPage.get(page) ?? []) {
            const flat = path.join(tmp, `pdf-${c.num}.png`);
            const t = thumbOf(c.num, () => fs.existsSync(flat) ? flat : flatten(c.file, c.mask, c.w, c.h, flat));
            const score = matchScore(pkgThumb, t);
            if (score < required) continue;
            // Colour variants (black/blue jacket) look alike in grayscale.
            const colorDistance = distance(pkgColor, colorOf(c.num, flat));
            if (colorDistance > MAX_COLOR_DISTANCE) continue;
            if (!best || score > best.score) best = { c, score, flat, colorDistance };
        }
        if (best && best.score >= 0.95) break;
    }

    const target = path.join(outImages, ref.name.replace(/\.\w+$/, '.jpg'));
    let entry;
    if (best) {
        const r = render(best.flat, best.c.enc === 'jpeg', MAX_UPSCALE, target);
        entry = { ...ref, status: 'pdf-original', score: +best.score.toFixed(3), source: `${best.c.w}x${best.c.h} p${best.c.page}#${best.c.num}${best.c.mask ? '+mask' : ''}`, ...r };
    } else {
        // The package picture is already enlarged from the PDF; clean it but do not enlarge it further.
        const flat = flatten(pkgFile, null, 0, 0, path.join(tmp, `pkg-${done}.png`));
        const r = render(flat, true, 1, target);
        entry = { ...ref, status: 'package-fallback', score: best ? +best.score.toFixed(3) : null, ...r };
    }

    report.push(entry);
    if (++done % 50 === 0) console.log(`${done}/${refs.length}`);
}

fs.writeFileSync(path.join(args.out, 'image-report.json'), JSON.stringify(report, null, 1));
fs.rmSync(tmp, { recursive: true, force: true });

const by = s => report.filter(x => x.status === s).length;
console.log(`done: ${report.length} pictures, ${by('pdf-original')} from PDF originals, ${by('package-fallback')} fallback, ${by('missing')} missing`);
