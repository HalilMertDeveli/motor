// node --test tools/image-audit
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { classify, drawnSize } from './render-checks.mjs';

const card = over => ({
    src: '/media/2/catalog/x.jpg?size=512&ver=5adf5754', cls: 'file-img art-picture', complete: true,
    naturalWidth: 512, naturalHeight: 512, boxWidth: 274, boxHeight: 274, objectFit: 'contain', transform: 'none',
    padding: [0, 0, 0, 0], dpr: 1, rect: { left: 0, top: 0, right: 274, bottom: 274 }, clip: { left: 0, top: 0, right: 274, bottom: 274 },
    ...over
});
const types = m => classify(m).map(d => d.type);

test('a contained product photo in a square card is fine', () => {
    assert.deepEqual(types(card()), []);
});

test('object-fit: cover on a non-matching box crops the product', () => {
    assert.deepEqual(types(card({ boxHeight: 206, objectFit: 'cover' })), ['cropped']);
});

test('object-fit: fill on a non-matching box stretches it', () => {
    assert.deepEqual(types(card({ boxHeight: 206, objectFit: 'fill' })), ['distorted']);
});

test('decorative hero banners may be cropped', () => {
    assert.deepEqual(types(card({ cls: 'motor-hero-photo', boxHeight: 100, objectFit: 'cover' })), []);
});

test('an image box larger than its clipping container is clipped', () => {
    assert.deepEqual(types(card({ rect: { left: -40, top: 0, right: 314, bottom: 274 } })), ['clipped']);
    // hover zoom (transform) is not a clip
    assert.deepEqual(types(card({ transform: 'matrix(1.045, 0, 0, 1.045, 0, 0)', rect: { left: -6, top: -6, right: 280, bottom: 280 } })), []);
});

test('a small file drawn large on a phone is soft', () => {
    assert.deepEqual(types(card({ naturalWidth: 256, naturalHeight: 256, dpr: 2 })), ['upscaled']);
    assert.deepEqual(types(card({ naturalWidth: 512, naturalHeight: 512, dpr: 2 })), []);
});

test('a dimmed decorative background may be enlarged, a product photo may not', () => {
    assert.deepEqual(types(card({ cls: 'motor-story-photo', naturalWidth: 800, naturalHeight: 1067, boxWidth: 927, boxHeight: 1236, objectFit: 'cover', dpr: 2, opacity: 0.45 })), []);
    assert.deepEqual(types(card({ naturalWidth: 800, naturalHeight: 1067, boxWidth: 927, boxHeight: 1236, dpr: 2, opacity: 0.45 })), ['upscaled']);
});

test('a media URL without the version hash is reported (stale cache)', () => {
    assert.deepEqual(types(card({ src: '/media/2/catalog/x.jpg?size=512' })), ['unversioned-url']);
    assert.deepEqual(types(card({ src: '/themes/motordark/images/hero-1280.webp' })), []);
});

test('a picture that did not decode is broken', () => {
    assert.deepEqual(types(card({ naturalWidth: 0, naturalHeight: 0 })), ['broken']);
});

test('contain keeps the aspect ratio inside padding', () => {
    const s = drawnSize(card({ naturalWidth: 1200, naturalHeight: 600, padding: [17, 17, 17, 17] }));
    assert.equal(Math.round(s.width), 240);
    assert.equal(Math.round(s.height), 120);
});
