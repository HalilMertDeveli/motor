// node --test tools/image-audit
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
    analyzePixels, chromaShare, colourConflict, dHash, duplicatesInProduct, fileDefects, hamming, isTruncated, readHeader,
    resample, samePhoto, severity
} from './image-lib.mjs';

// White RGB canvas with filled rectangles of a colour
function canvas(w, h) {
    const data = Buffer.alloc(w * h * 3, 255);
    return {
        w, h, data,
        rect(x0, y0, x1, y1, rgb) {
            for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) data.set(rgb, (y * w + x) * 3);
            return this;
        }
    };
}
const GREY = [90, 90, 90], RED = [200, 30, 30], BLUE = [30, 30, 200];

// Minimal headers
const jpeg = (w, h, eoi = true) => Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x04, 0, 0, 0xff, 0xc0, 0x00, 0x11, 0x08, h >> 8, h & 255, w >> 8, w & 255, 3, ...Array(9).fill(0), ...(eoi ? [0xff, 0xd9] : [0, 0])]);
function png(w, h) {
    const b = Buffer.alloc(33 + 12);
    b.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], 0);
    b.writeUInt32BE(13, 8); b.write('IHDR', 12); b.writeUInt32BE(w, 16); b.writeUInt32BE(h, 20);
    b.write('IEND', 37);
    return b;
}

test('reads JPEG and PNG dimensions', () => {
    assert.deepEqual(readHeader(jpeg(1200, 800)), { format: 'jpeg', width: 1200, height: 800 });
    assert.deepEqual(readHeader(png(280, 50)), { format: 'png', width: 280, height: 50 });
});

test('rejects files that are not images', () => {
    assert.equal(readHeader(Buffer.from('<html>not found</html>')), null);
    assert.equal(readHeader(Buffer.alloc(4)), null);
});

test('detects a truncated JPEG', () => {
    assert.equal(isTruncated(jpeg(10, 10, true), 'jpeg'), false);
    assert.equal(isTruncated(jpeg(10, 10, false), 'jpeg'), true);
});

test('a centred product on white is complete and well framed', () => {
    const p = analyzePixels(canvas(100, 100).rect(10, 10, 89, 89, GREY));
    assert.equal(p.studio, true);
    assert.deepEqual(p.cut, []);
    assert.equal(p.boxFill, 0.8);
});

test('a product running off the frame is reported per side', () => {
    const p = analyzePixels(canvas(100, 100).rect(0, 20, 70, 99, GREY));
    assert.deepEqual(p.cut.sort(), ['bottom', 'left']);
});

test('lifestyle photos (no white background) are not judged for cut edges', () => {
    const p = analyzePixels(canvas(100, 100).rect(0, 0, 99, 99, BLUE).rect(30, 30, 60, 60, GREY));
    assert.equal(p.studio, false);
    assert.deepEqual(p.cut, []);
});

test('rules: missing, invalid, low resolution, cut, tiny, stale thumbnail', () => {
    assert.equal(fileDefects({ exists: false })[0].code, 'missing-file');
    assert.equal(fileDefects({ exists: true, header: null })[0].code, 'invalid-file');
    const base = { exists: true, header: { width: 1200, height: 1200 }, bytes: 10, dbSize: 10, dbWidth: 1200, dbHeight: 1200 };
    assert.deepEqual(fileDefects(base), []);
    assert.equal(fileDefects({ ...base, header: { width: 300, height: 300 }, dbWidth: 300, dbHeight: 300 })[0].code, 'low-resolution');
    assert.equal(fileDefects({ ...base, dbWidth: 1000 })[0].code, 'db-size-mismatch');
    assert.equal(fileDefects({ ...base, pixels: { studio: true, cut: ['left'], boxFill: 0.9 } })[0].code, 'subject-cut');
    assert.equal(fileDefects({ ...base, pixels: { studio: true, cut: [], boxFill: 0.25 } })[0].code, 'subject-tiny');
    assert.equal(fileDefects({ ...base, thumbDiff: 40 })[0].code, 'stale-thumbnail');
    assert.equal(severity('stale-thumbnail'), 'error');
    assert.equal(severity('subject-cut'), 'review');
});

test('a SHAD kit cropped to the rear of the bike (one cut side) is expected', () => {
    const base = { exists: true, header: { width: 1200, height: 1200 }, kit: true, pixels: { studio: true, cut: ['right'], boxFill: 1 } };
    assert.deepEqual(fileDefects(base), []);
    assert.equal(fileDefects({ ...base, pixels: { ...base.pixels, cut: ['right', 'top'] } })[0].code, 'subject-cut');
});

test('hashes: same picture matches after resizing, a different one does not', () => {
    const a = canvas(120, 120).rect(20, 30, 100, 90, GREY);
    const b = resample(a, 60, 60);
    const c = canvas(120, 120).rect(60, 10, 110, 110, GREY);
    assert.ok(hamming(dHash(a), dHash(b)) <= 4);
    assert.ok(hamming(dHash(a), dHash(c)) > 10);
});

test('colour variants in the same pose are not duplicates; a re-upload is', () => {
    const shot = rgb => resample(canvas(96, 96).rect(20, 20, 75, 75, GREY).rect(30, 30, 45, 70, rgb), 96, 96);
    const red = { mediaId: 1, thumb: shot(RED) }, red2 = { mediaId: 2, thumb: shot(RED) }, blue = { mediaId: 3, thumb: shot(BLUE) };
    for (const p of [red, red2, blue]) p.hash = dHash(p.thumb);
    assert.ok(chromaShare(red.thumb, blue.thumb) > 0.01);
    assert.equal(samePhoto(red, blue), false);
    assert.equal(samePhoto(red, red2), true);
    assert.deepEqual(duplicatesInProduct([red, blue, red2]), [[1, 2]]);
});

test('colour names in product names', () => {
    assert.equal(colourConflict('Scoyco MC117W Lady Siyah Kadın Eldiveni', 'Scoyco MC117W Lady Kırmızı Kadın Eldiveni'), true);
    assert.equal(colourConflict('MTS X340 Mat Black Kask', 'MTS X340 Black Kask'), false);
    assert.equal(colourConflict('SHAD SH40 Çanta', 'SHAD SH45 Çanta'), false);
});
