// MOTOR: picture helpers shared by the supplier picture tools (ffmpeg I/O, matching, product box).

import { execFileSync } from 'node:child_process';

export const MATCH_WIDTH = 96;  // width of the grayscale thumbnails used for matching
export const WHITE = 238;       // a pixel darker than this in any channel belongs to the product

export function ffmpeg(argv, opts = {}) {
    return execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...argv], { maxBuffer: 256 * 1024 * 1024, ...opts });
}

export function rawRgb(file) {
    const probe = execFileSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file]).toString().trim().split(',');
    const w = +probe[0], h = +probe[1];
    return { w, h, data: ffmpeg(['-i', file, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-']) };
}

export function grayThumb(file) {
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

// Best NCC of `tpl` placed anywhere inside `img` (tpl must fit), sampling every `step` pixels.
function bestPlacement(img, tpl, step = 2) {
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

function placementAt(img, tpl, scale, step) {
    const tw = Math.round(img.w * scale), th = Math.round(tw * tpl.h / tpl.w);
    if (tw < 16 || th < 16 || tw > img.w || th > img.h) return -1;
    return bestPlacement(img, resample(tpl, tw, th), step);
}

export function matchScore(a, b) {
    // Either picture may be a crop of the other.
    // Coarse search over scales, then a fine search (1 % scale, 1 px) around the best coarse result.
    let best = { score: -1 };
    for (let s = 1.0; s >= 0.35; s -= 0.05) {
        for (const pair of [[b, a], [a, b]]) {
            const score = placementAt(pair[0], pair[1], s, 2);
            if (score > best.score) best = { score, s, pair };
        }
    }
    if (best.score < 0.5) return best.score;

    let fine = best.score;
    for (let s = best.s - 0.05; s <= best.s + 0.05; s += 0.01) {
        fine = Math.max(fine, placementAt(best.pair[0], best.pair[1], s, 1));
    }
    return fine;
}

// ---------------------------------------------------------------- product pixels

export function productBox({ w, h, data }) {
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

// Mean colour of the product pixels (non-white), on a small thumbnail.
export function colorSignature(file) {
    const data = ffmpeg(['-i', file, '-vf', 'scale=48:-1:flags=area,format=rgb24', '-f', 'rawvideo', '-']);
    let r = 0, g = 0, b = 0, n = 0;
    for (let i = 0; i + 2 < data.length; i += 3) {
        if (data[i] < WHITE || data[i + 1] < WHITE || data[i + 2] < WHITE) {
            r += data[i]; g += data[i + 1]; b += data[i + 2]; n++;
        }
    }
    return n ? [r / n, g / n, b / n] : [255, 255, 255];
}

export const colorDistance = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
