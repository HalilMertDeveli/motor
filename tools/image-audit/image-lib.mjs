// Pure helpers for the image inventory: header parsing, pixel analysis, perceptual hashes and the defect rules.
// No I/O here, so every rule is unit tested (image-lib.test.mjs).

// --- Header parsing ------------------------------------------------------------------------------------------------

// Width/height and format from the first bytes of a JPEG, PNG or WebP file, or null when the header is invalid.
export function readHeader(buf) {
    if (!buf || buf.length < 12) return null;
    if (buf[0] === 0x89 && buf.toString('ascii', 1, 4) === 'PNG') {
        return buf.length >= 24 ? { format: 'png', width: buf.readUInt32BE(16), height: buf.readUInt32BE(20) } : null;
    }
    if (buf.toString('ascii', 0, 4) === 'RIFF' && buf.toString('ascii', 8, 12) === 'WEBP') {
        const chunk = buf.toString('ascii', 12, 16);
        if (chunk === 'VP8X') return { format: 'webp', width: 1 + buf.readUIntLE(24, 3), height: 1 + buf.readUIntLE(27, 3) };
        if (chunk === 'VP8 ') return { format: 'webp', width: buf.readUInt16LE(26) & 0x3fff, height: buf.readUInt16LE(28) & 0x3fff };
        if (chunk === 'VP8L') {
            const b = buf.readUInt32LE(21);
            return { format: 'webp', width: 1 + (b & 0x3fff), height: 1 + ((b >> 14) & 0x3fff) };
        }
        return null;
    }
    if (buf[0] === 0xff && buf[1] === 0xd8) {
        let i = 2;
        while (i + 9 < buf.length) {
            if (buf[i] !== 0xff) { i++; continue; }
            const marker = buf[i + 1];
            if (marker === 0xd8 || marker === 0x01 || (marker >= 0xd0 && marker <= 0xd7)) { i += 2; continue; }
            const len = buf.readUInt16BE(i + 2);
            // SOF0..SOF15 except DHT (C4), JPG (C8) and DAC (CC)
            if (marker >= 0xc0 && marker <= 0xcf && ![0xc4, 0xc8, 0xcc].includes(marker)) {
                return { format: 'jpeg', width: buf.readUInt16BE(i + 7), height: buf.readUInt16BE(i + 5) };
            }
            i += 2 + len;
        }
        return null;
    }
    return null;
}

// A complete JPEG ends with the EOI marker; a truncated download does not.
export function isTruncated(buf, format) {
    if (format === 'jpeg') {
        // Some encoders pad after EOI; look at the last bytes
        const tail = buf.subarray(Math.max(0, buf.length - 64));
        for (let i = tail.length - 2; i >= 0; i--) if (tail[i] === 0xff && tail[i + 1] === 0xd9) return false;
        return true;
    }
    if (format === 'png') return buf.toString('ascii', buf.length - 8, buf.length - 4) !== 'IEND';
    return false;
}

// --- Pixel analysis ------------------------------------------------------------------------------------------------

const LIGHT = 232;   // all channels at least this: studio background
const BORDER_RUN = 0.08;   // a side "cuts" the subject when this share of its pixels is subject

const isBg = (d, i) => d[i] >= LIGHT && d[i + 1] >= LIGHT && d[i + 2] >= LIGHT;

// Subject box, fill and border contact on RGB pixels ({ w, h, data } with 3 bytes per pixel).
export function analyzePixels({ w, h, data }) {
    const at = (x, y) => (y * w + x) * 3;
    const corners = [[0, 0], [w - 1, 0], [0, h - 1], [w - 1, h - 1]].filter(([x, y]) => isBg(data, at(x, y))).length;
    const studio = corners >= 3;
    let minX = w, minY = h, maxX = -1, maxY = -1, subject = 0;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
        if (isBg(data, at(x, y))) continue;
        subject++;
        if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
    }
    if (maxX < 0) return { studio, empty: true, fill: 0, boxFill: 0, cut: [] };

    // Share of subject pixels on each image edge
    const side = (n, f) => { let c = 0; for (let k = 0; k < n; k++) if (!isBg(data, f(k))) c++; return c / n; };
    const edges = {
        top: side(w, x => at(x, 0)), bottom: side(w, x => at(x, h - 1)),
        left: side(h, y => at(0, y)), right: side(h, y => at(w - 1, y))
    };
    const cut = studio ? Object.keys(edges).filter(k => edges[k] >= BORDER_RUN) : [];
    const bw = maxX - minX + 1, bh = maxY - minY + 1;
    return {
        studio,
        empty: false,
        fill: +(subject / (w * h)).toFixed(3),
        // Largest side of the subject box relative to the image: how much of the frame the product uses
        boxFill: +Math.max(bw / w, bh / h).toFixed(3),
        box: { x: minX, y: minY, w: bw, h: bh },
        edges: Object.fromEntries(Object.entries(edges).map(([k, v]) => [k, +v.toFixed(3)])),
        cut
    };
}

// 64-bit difference hash (hex) from RGB pixels: grey 9x8 box filter, then left/right comparisons.
export function dHash({ w, h, data }) {
    const g = new Float64Array(72);
    for (let gy = 0; gy < 8; gy++) for (let gx = 0; gx < 9; gx++) {
        const x0 = Math.floor(gx * w / 9), x1 = Math.max(x0 + 1, Math.floor((gx + 1) * w / 9));
        const y0 = Math.floor(gy * h / 8), y1 = Math.max(y0 + 1, Math.floor((gy + 1) * h / 8));
        let s = 0, n = 0;
        for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
            const i = (y * w + x) * 3;
            s += 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2]; n++;
        }
        g[gy * 9 + gx] = s / n;
    }
    let bits = 0n;
    for (let y = 0; y < 8; y++) for (let x = 0; x < 8; x++) bits = (bits << 1n) | (g[y * 9 + x] > g[y * 9 + x + 1] ? 1n : 0n);
    return bits.toString(16).padStart(16, '0');
}

const popcount = x => { x -= (x >>> 1) & 0x55555555; x = (x & 0x33333333) + ((x >>> 2) & 0x33333333); return (((x + (x >>> 4)) & 0x0f0f0f0f) * 0x01010101) >>> 24; };

// Differing bits of two 64-bit hex hashes
export function hamming(a, b) {
    return popcount((parseInt(a.slice(0, 8), 16) ^ parseInt(b.slice(0, 8), 16)) >>> 0) + popcount((parseInt(a.slice(8), 16) ^ parseInt(b.slice(8), 16)) >>> 0);
}

// Box-filter resample of RGB pixels to w x h (aspect not preserved)
export function resample(src, w, h) {
    const data = Buffer.alloc(w * h * 3);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
        const x0 = Math.floor(x * src.w / w), x1 = Math.max(x0 + 1, Math.floor((x + 1) * src.w / w));
        const y0 = Math.floor(y * src.h / h), y1 = Math.max(y0 + 1, Math.floor((y + 1) * src.h / h));
        for (let c = 0; c < 3; c++) {
            let s = 0, n = 0;
            for (let yy = y0; yy < y1; yy++) for (let xx = x0; xx < x1; xx++) { s += src.data[(yy * src.w + xx) * 3 + c]; n++; }
            data[(y * w + x) * 3 + c] = Math.round(s / n);
        }
    }
    return { w, h, data };
}

// Mean absolute difference (0..255) of two RGB buffers of the same size.
export function meanDiff(a, b) {
    if (a.w !== b.w || a.h !== b.h) return Infinity;
    let s = 0;
    for (let i = 0; i < a.data.length; i++) s += Math.abs(a.data[i] - b.data[i]);
    return s / a.data.length;
}

// Share of pixels whose chroma (Cb/Cr) differs noticeably: separates colour variants that share a pose
export function chromaShare(a, b) {
    if (a.w !== b.w || a.h !== b.h) return 1;
    const cbcr = (d, i) => [-0.169 * d[i] - 0.331 * d[i + 1] + 0.5 * d[i + 2], 0.5 * d[i] - 0.419 * d[i + 1] - 0.081 * d[i + 2]];
    let n = 0;
    for (let i = 0; i < a.data.length; i += 3) {
        const [p, q] = cbcr(a.data, i), [r, t] = cbcr(b.data, i);
        if (Math.hypot(p - r, q - t) > 12) n++;
    }
    return n / (a.w * a.h);
}

// Same photo (re-encoded or resized), not a colour variant or a second angle. Calibrated on the Motosev/SHAD pictures:
// colour variants differ in chroma on >= 0.8 % of the pixels, black/silver variants by a mean of >= 2.9.
export function samePhoto(a, b) {
    if (a.hash && b.hash && hamming(a.hash, b.hash) > DUPLICATE_DISTANCE) return false;
    return meanDiff(a.thumb, b.thumb) < 1.5 && chromaShare(a.thumb, b.thumb) < 0.002;
}

// --- Rules ---------------------------------------------------------------------------------------------------------

export const MIN_SIDE = 600;        // below: soft on the 1024 px product page
export const LOW_SIDE = 400;        // below: soft even on product cards
export const TINY_SUBJECT = 0.45;   // subject box uses less than this of the frame
export const MAX_ASPECT = 2.2;      // wider/taller than this does not fit square cards
export const DUPLICATE_DISTANCE = 4;  // dHash bits
export const STALE_DIFF = 18;       // mean pixel difference between original and its thumbnail

// Defects of one stored picture. `f` carries db/file/pixel facts gathered by inventory.mjs.
export function fileDefects(f) {
    const d = [];
    if (!f.exists) return [{ code: 'missing-file', detail: 'storage file not found' }];
    if (!f.header) return [{ code: 'invalid-file', detail: 'not a JPEG/PNG/WebP header' }];
    if (f.truncated) d.push({ code: 'truncated', detail: 'file ends before the image end marker' });
    if (f.decodeError) d.push({ code: 'decode-failed', detail: f.decodeError });
    if (f.dbWidth && (f.dbWidth !== f.header.width || f.dbHeight !== f.header.height)) {
        d.push({ code: 'db-size-mismatch', detail: `db ${f.dbWidth}x${f.dbHeight}, file ${f.header.width}x${f.header.height}` });
    }
    if (f.dbSize && f.bytes !== f.dbSize) d.push({ code: 'db-length-mismatch', detail: `db ${f.dbSize} bytes, file ${f.bytes}` });
    const short = Math.min(f.header.width, f.header.height), aspect = f.header.width / f.header.height;
    if (short < LOW_SIDE) d.push({ code: 'low-resolution', detail: `${f.header.width}x${f.header.height}` });
    else if (short < MIN_SIDE) d.push({ code: 'below-detail-size', detail: `${f.header.width}x${f.header.height}` });
    if (aspect > MAX_ASPECT || aspect < 1 / MAX_ASPECT) d.push({ code: 'extreme-aspect', detail: aspect.toFixed(2) });
    const p = f.pixels;
    if (p && !p.empty) {
        // Mounting kits are cropped to the rear of the bike on purpose (crop-bike-photos.mjs): one cut side is expected
        if (p.cut.length && !(f.kit && p.cut.length === 1)) d.push({ code: 'subject-cut', detail: `product runs off the ${p.cut.join('/')} edge` });
        if (p.studio && p.boxFill < TINY_SUBJECT) d.push({ code: 'subject-tiny', detail: `product uses ${Math.round(p.boxFill * 100)}% of the frame` });
    }
    if (p?.empty) d.push({ code: 'blank', detail: 'no product pixels' });
    if (f.wholeBike) d.push({ code: 'kit-shows-whole-bike', detail: 'mounting kit pictured as a whole motorcycle' });
    if (f.thumbDiff !== undefined && f.thumbDiff > STALE_DIFF) d.push({ code: 'stale-thumbnail', detail: `thumbnail differs from the original (${f.thumbDiff.toFixed(1)})` });
    return d;
}

// Severity: blocking defects break or misrepresent the product; review items need a human look.
export const BLOCKING = new Set(['missing-file', 'invalid-file', 'truncated', 'decode-failed', 'blank', 'stale-thumbnail', 'duplicate-in-product', 'db-size-mismatch', 'main-picture-stale', 'dangling-reference', 'duplicate-mapping', 'published-without-picture', 'shared-photo-colour-conflict']);
export const severity = code => BLOCKING.has(code) ? 'error' : 'review';

// Pictures of one product that look the same (re-uploads), as [keepMediaId, dupeMediaId] pairs.
export function duplicatesInProduct(pictures) {
    const pairs = [];
    for (let i = 0; i < pictures.length; i++) for (let j = i + 1; j < pictures.length; j++) {
        const a = pictures[i], b = pictures[j];
        if (a.thumb && b.thumb && samePhoto(a, b)) pairs.push([a.mediaId, b.mediaId]);
    }
    return pairs;
}

// Colour words in Turkish product names; two products with different colours must not share a photo.
const COLOURS = ['siyah', 'beyaz', 'kırmızı', 'mavi', 'sarı', 'yeşil', 'gri', 'turuncu', 'mor', 'pembe', 'lacivert', 'kahverengi', 'bej', 'antrasit', 'füme', 'mat', 'parlak', 'black', 'white', 'red', 'blue', 'yellow', 'green', 'grey', 'gray', 'orange', 'fluo', 'neon'];
export function colours(name) {
    const words = String(name || '').toLocaleLowerCase('tr-TR').split(/[^\p{L}]+/u);
    return COLOURS.filter(c => words.includes(c)).sort();
}

// Same photo on products whose names name different colours: the photo cannot be right for both.
export function colourConflict(nameA, nameB) {
    const a = colours(nameA).filter(c => c !== 'mat' && c !== 'parlak'), b = colours(nameB).filter(c => c !== 'mat' && c !== 'parlak');
    return a.length > 0 && b.length > 0 && a.join() !== b.join();
}
